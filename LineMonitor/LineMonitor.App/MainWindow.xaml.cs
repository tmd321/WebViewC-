using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LineMonitor.App.ViewModels;
using Line = System.Windows.Shapes.Line;

namespace LineMonitor.App;

/// <summary>
/// Self-built dashboard shell (AvalonDock was tried first but its net40-only assembly
/// rendered with dated/broken chrome under net8 and had a reordering bug — this is a
/// small hand-rolled replacement styled with our own resources).
/// Boxes live on a Canvas at their own X/Y — resizing the window never reflows them
/// (a WrapPanel did that at first and it was explicitly unwanted). Dragging a title bar
/// moves that one card up to other cards' boundaries; there's no swap-on-drop or auto layout.
/// The canvas starts empty — the left sidebar lists every box type as a catalog the user
/// drags (or double-clicks) onto the canvas; already-placed ones show dimmed and can't be
/// re-added, closing (✕) a card un-dims its row so it can be added back. Position/size/
/// visibility persists per site/process/electrode/preset selection under %LocalAppData%\LineMonitor.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string LayoutDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LineMonitor");

    private const double DefaultCardWidth = 480;
    private const double DefaultCardHeight = 420;
    private const double MinCardWidth = 260;
    private const double MinCardHeight = 180;

    private readonly ObservableCollection<DashboardBoxModel> _visibleBoxes = new();
    private readonly List<DashboardBoxModel> _allBoxes = new();

    private bool _sidebarExpanded = true;
    private double _sidebarExpandedWidth = 228;
    private int _selectedLayoutPreset = 1;
    private Point? _catalogDragStart;
    private DashboardBoxModel? _catalogDragCandidate;

    private readonly HashSet<DashboardBoxModel> _selection = new();
    private DashboardBoxModel? _selectionAnchor;
    private int _nextZIndex = 1;

    public MainWindow()
    {
        InitializeComponent();
        _visibleBoxes.CollectionChanged += (_, _) =>
            EmptyCanvasHint.Visibility = _visibleBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 마우스 버튼이 이미 떼진 상태에서 호출되면 여기로 옴 — 무시하고 지나감.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;

        _allBoxes.Add(CreateBox("TopMeasurement", "TOP 측정",
            BindControl(new Controls.SidePanel { Title = "TOP" }, vm, "LiveOverview.TopMeasurement")));
        _allBoxes.Add(CreateBox("BackMeasurement", "BACK 측정",
            BindControl(new Controls.SidePanel { Title = "BACK" }, vm, "LiveOverview.BackMeasurement")));
        _allBoxes.Add(CreateBox("WetMismatch", "Wet Mismatch",
            BindControl(new Controls.MismatchPanel { Title = "Wet Mismatch", LeftLabel = "DS", RightLabel = "OS" }, vm, "LiveOverview.WetMismatchLanes")));
        _allBoxes.Add(CreateBox("DryMismatch", "Dry Mismatch",
            BindControl(new Controls.MismatchPanel { Title = "Dry Mismatch", LeftLabel = "OS", RightLabel = "DS" }, vm, "LiveOverview.DryMismatchLanes")));
        _allBoxes.Add(CreateBox("TrendCpk", "추세 / CPK",
            BindControl(new Controls.TrendCpkPanel(), vm, "TrendCpk")));
        _allBoxes.Add(CreateBox("DefectThumbs", "불량 이미지",
            BindControl(new Controls.DefectThumbGrid(), vm, "DefectHistory")));
        _allBoxes.Add(CreateBox("DefectPivot", "개소 x 불량유형",
            BindControl(new Controls.DefectPivotTable(), vm, "DefectHistory")));

        BoxesPanel.ItemsSource = _visibleBoxes;
        CatalogList.ItemsSource = _allBoxes;
        ResetLayout();
        RestoreSavedLayout(vm);
    }

    private static FrameworkElement BindControl(FrameworkElement control, object source, string path)
    {
        if (control is Controls.SidePanel sidePanel)
        {
            sidePanel.SetBinding(Controls.SidePanel.ShowInsulationProperty,
                new Binding(nameof(MainViewModel.ShowInsulation)) { Source = source });
        }

        if (control is Controls.MismatchPanel mismatchPanel)
        {
            mismatchPanel.SetBinding(Controls.MismatchPanel.IsPositiveElectrodeProperty,
                new Binding(nameof(MainViewModel.ShowInsulation)) { Source = source });
        }

        control.SetBinding(DataContextProperty, new Binding(path) { Source = source });
        return control;
    }

    private DashboardBoxModel CreateBox(string id, string title, FrameworkElement content)
    {
        var model = new DashboardBoxModel { Id = id, Title = title };
        model.Card = BuildCard(model, content);
        return model;
    }

    private Border BuildCard(DashboardBoxModel model, FrameworkElement content)
    {
        var titleText = new TextBlock
        {
            Text = model.Title,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextBrush"]
        };

        // 크기 맞춤 기준(마지막 Ctrl+클릭한 박스)일 때만 보이는 배지 — RefreshSelectionVisual이 토글.
        var anchorBadge = new TextBlock
        {
            Text = "기준",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources["WarnBrush"],
            Visibility = Visibility.Collapsed
        };
        model.AnchorBadge = anchorBadge;

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(anchorBadge);

        Border card = null!; // BuildCard 끝에서 채워짐 — 아래 클로저들이 참조함(가로/세로 맞춤, 리사이즈 그립)

        var fitWidthButton = new Button
        {
            Content = "⇔",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "가로 기본 크기로"
        };
        fitWidthButton.Click += (_, _) => ResizeBox(model, DefaultCardWidth, card.Height);

        var fitHeightButton = new Button
        {
            Content = "⇕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "세로 기본 크기로"
        };
        fitHeightButton.Click += (_, _) => ResizeBox(model, card.Width, DefaultCardHeight);

        var closeButton = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "닫기 (사이드바에서 다시 추가 가능)"
        };
        closeButton.Click += (_, _) => CloseBox(model);

        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleStack, 0);
        Grid.SetColumn(fitWidthButton, 1);
        Grid.SetColumn(fitHeightButton, 2);
        Grid.SetColumn(closeButton, 3);
        titleGrid.Children.Add(titleStack);
        titleGrid.Children.Add(fitWidthButton);
        titleGrid.Children.Add(fitHeightButton);
        titleGrid.Children.Add(closeButton);

        var titleBar = new Border
        {
            Background = (Brush)Application.Current.Resources["BorderBrush2"],
            Padding = new Thickness(8, 5, 4, 5),
            Cursor = Cursors.SizeAll,
            Child = titleGrid
        };

        // 제목바 드래그 = 다른 박스 경계까지 이동(스왑 아님, 다른 박스의 위치는 유지).
        // Ctrl+클릭이면 이동 대신 다중 선택 토글(MFC 다이얼로그 에디터처럼) — "크기 맞춤" 버튼들이 이 선택을 씀.
        Point? moveStart = null;
        double moveStartX = 0, moveStartY = 0;
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                ToggleSelection(model, card);
                e.Handled = true;
                return;
            }

            if (!_selection.Contains(model))
            {
                ClearSelection();
            }

            moveStart = e.GetPosition(null);
            moveStartX = model.X;
            moveStartY = model.Y;
            titleBar.CaptureMouse();
            e.Handled = true;
        };
        titleBar.MouseMove += (_, e) =>
        {
            if (moveStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pos = e.GetPosition(null);
            MoveBox(model, Math.Max(0, moveStartX + (pos.X - start.X)),
                Math.Max(0, moveStartY + (pos.Y - start.Y)));
        };
        titleBar.MouseLeftButtonUp += (_, _) =>
        {
            moveStart = null;
            titleBar.ReleaseMouseCapture();
        };

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };

        var body = new DockPanel();
        DockPanel.SetDock(titleBar, Dock.Top);
        body.Children.Add(titleBar);
        body.Children.Add(scroller);

        // 우측 하단 리사이즈 그립: 드래그로 카드 크기 직접 조절(자동맞춤 대신 수동).
        var grip = new Border
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "드래그해서 크기 조절"
        };
        var gripCanvas = new Canvas { Width = 10, Height = 10 };
        for (var i = 0; i < 3; i++)
        {
            var offset = 2.0 + i * 3.5;
            gripCanvas.Children.Add(new Line
            {
                X1 = offset, Y1 = 10, X2 = 10, Y2 = offset,
                Stroke = (Brush)Application.Current.Resources["MutedBrush"],
                StrokeThickness = 1.2
            });
        }
        grip.Child = gripCanvas;

        Point? resizeStart = null;
        double resizeStartWidth = 0, resizeStartHeight = 0;
        grip.MouseLeftButtonDown += (_, e) =>
        {
            resizeStart = e.GetPosition(null);
            resizeStartWidth = card.ActualWidth;
            resizeStartHeight = card.ActualHeight;
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (resizeStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pos = e.GetPosition(null);
            ResizeBox(model, Math.Max(MinCardWidth, resizeStartWidth + (pos.X - start.X)),
                Math.Max(MinCardHeight, resizeStartHeight + (pos.Y - start.Y)));
        };
        grip.MouseLeftButtonUp += (_, _) =>
        {
            resizeStart = null;
            grip.ReleaseMouseCapture();
        };

        var cardGrid = new Grid();
        cardGrid.Children.Add(body);
        cardGrid.Children.Add(grip);

        card = new Border
        {
            Width = DefaultCardWidth,
            Height = DefaultCardHeight,
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush2"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = cardGrid
        };

        // 카드 어디를 클릭하든(제목바/본문/그립 상관없이) 맨 앞으로 — 나중에 만든 박스가 항상
        // 위에 있던 문제 수정. PreviewMouseLeftButtonDown은 titleBar/grip의 개별 핸들러보다
        // 먼저 도는 터널링 이벤트라 e.Handled 안 건드리면 기존 드래그/선택 동작에 영향 없음.
        card.PreviewMouseLeftButtonDown += (_, _) => BringToFront(model);

        return card;
    }

    private IEnumerable<Rect> OtherBoxBounds(DashboardBoxModel model) =>
        _visibleBoxes.Where(other => other != model && other.Card is FrameworkElement)
            .Select(other => new Rect(other.X, other.Y,
                ((FrameworkElement)other.Card).Width, ((FrameworkElement)other.Card).Height));

    private static bool Overlaps(double start, double length, double otherStart, double otherLength) =>
        start < otherStart + otherLength && start + length > otherStart;

    private static double ClampMovement(double current, double target, double length, double otherStart, double otherLength)
    {
        // 이동 경로의 경계도 검사해 빠르게 드래그해도 다른 박스를 통과하지 않게 한다.
        if (current + length <= otherStart && target > current)
            return Math.Min(target, otherStart - length);
        if (current >= otherStart + otherLength && target < current)
            return Math.Max(target, otherStart + otherLength);
        return target;
    }

    private void MoveBox(DashboardBoxModel model, double x, double y)
    {
        if (model.Card is not FrameworkElement card) return;
        var obstacles = OtherBoxBounds(model).ToList();
        foreach (var bounds in obstacles)
        {
            if (Overlaps(model.Y, card.Height, bounds.Top, bounds.Height))
                x = ClampMovement(model.X, x, card.Width, bounds.Left, bounds.Width);
        }
        model.X = x;
        foreach (var bounds in obstacles)
        {
            if (Overlaps(model.X, card.Width, bounds.Left, bounds.Width))
                y = ClampMovement(model.Y, y, card.Height, bounds.Top, bounds.Height);
        }
        model.Y = y;
    }

    private void ResizeBox(DashboardBoxModel model, double width, double height)
    {
        if (model.Card is not FrameworkElement card) return;
        var obstacles = OtherBoxBounds(model).ToList();
        foreach (var bounds in obstacles)
        {
            if (width > card.Width && Overlaps(model.Y, card.Height, bounds.Top, bounds.Height))
                width = ClampMovement(model.X + card.Width, model.X + width, 0,
                    bounds.Left, bounds.Width) - model.X;
        }
        card.Width = width;
        foreach (var bounds in obstacles)
        {
            if (height > card.Height && Overlaps(model.X, card.Width, bounds.Left, bounds.Width))
                height = ClampMovement(model.Y + card.Height, model.Y + height, 0,
                    bounds.Top, bounds.Height) - model.Y;
        }
        card.Height = height;
    }

    private void BringToFront(DashboardBoxModel model) => model.ZIndex = _nextZIndex++;

    private void CloseBox(DashboardBoxModel model)
    {
        _visibleBoxes.Remove(model);
        model.IsPlaced = false;
        _selection.Remove(model);
        if (_selectionAnchor == model)
        {
            _selectionAnchor = null;
        }

        UpdateSizeMatchButtonsEnabled();
    }

    private void ToggleSelection(DashboardBoxModel model, Border card)
    {
        var previousAnchor = _selectionAnchor;

        if (_selection.Contains(model))
        {
            _selection.Remove(model);
            if (_selectionAnchor == model)
            {
                // 기준이었던 박스를 해제하면 기준도 같이 사라짐 — 남은 것 중 하나를 임의로
                // 승격시키지 않음(헷갈림 방지). 다시 Ctrl+클릭해서 새 기준 지정하면 됨.
                _selectionAnchor = null;
            }
        }
        else
        {
            _selection.Add(model);
            _selectionAnchor = model; // 마지막으로 Ctrl+클릭한 박스가 크기 맞춤 기준(주황 테두리+"기준" 배지).
        }

        RefreshSelectionVisual(model);
        if (previousAnchor is not null && previousAnchor != _selectionAnchor)
        {
            RefreshSelectionVisual(previousAnchor); // 기준이 바뀌었으니 이전 기준 박스는 일반 선택 표시로 되돌림.
        }

        UpdateSizeMatchButtonsEnabled();
    }

    private void ClearSelection()
    {
        var affected = _selection.ToList();
        _selection.Clear();
        _selectionAnchor = null;
        foreach (var model in affected)
        {
            RefreshSelectionVisual(model);
        }

        UpdateSizeMatchButtonsEnabled();
    }

    private void RefreshSelectionVisual(DashboardBoxModel model)
    {
        if (model.Card is not Border card)
        {
            return;
        }

        var isSelected = _selection.Contains(model);
        var isAnchor = isSelected && model == _selectionAnchor;

        card.BorderBrush = (Brush)Application.Current.Resources[isAnchor ? "WarnBrush" : isSelected ? "AccentBrush" : "BorderBrush2"];
        card.BorderThickness = new Thickness(isAnchor ? 3 : isSelected ? 2 : 1);

        if (model.AnchorBadge is UIElement badge)
        {
            badge.Visibility = isAnchor ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateSizeMatchButtonsEnabled()
    {
        var enabled = _selection.Count >= 2 && _selectionAnchor is not null;
        MatchWidthButton.IsEnabled = enabled;
        MatchHeightButton.IsEnabled = enabled;
        MatchBothButton.IsEnabled = enabled;
    }

    private void OnMatchWidthClick(object sender, RoutedEventArgs e) => ApplySizeMatch(matchWidth: true, matchHeight: false);

    private void OnMatchHeightClick(object sender, RoutedEventArgs e) => ApplySizeMatch(matchWidth: false, matchHeight: true);

    private void OnMatchBothClick(object sender, RoutedEventArgs e) => ApplySizeMatch(matchWidth: true, matchHeight: true);

    private void ApplySizeMatch(bool matchWidth, bool matchHeight)
    {
        if (_selectionAnchor is not { } anchor || anchor.Card is not FrameworkElement anchorFe || _selection.Count < 2)
        {
            return;
        }

        foreach (var model in _selection)
        {
            if (model == anchor || model.Card is not FrameworkElement fe)
            {
                continue;
            }

            ResizeBox(model, matchWidth ? anchorFe.ActualWidth : fe.Width,
                matchHeight ? anchorFe.ActualHeight : fe.Height);
        }
    }

    private void OnCatalogItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CatalogList.SelectedItem is not DashboardBoxModel model || model.IsPlaced)
        {
            return;
        }

        model.IsPlaced = true;
        BringToFront(model);
        _visibleBoxes.Add(model);
    }

    private void OnCatalogRowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DashboardBoxModel { IsPlaced: false } model)
        {
            return;
        }

        _catalogDragStart = e.GetPosition(null);
        _catalogDragCandidate = model;
    }

    private void OnCatalogRowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _catalogDragCandidate is not { } model || _catalogDragStart is not { } start)
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - start.X) < 6 && Math.Abs(pos.Y - start.Y) < 6)
        {
            return;
        }

        _catalogDragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, model, DragDropEffects.Copy);
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(DashboardBoxModel)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(DashboardBoxModel)) is not DashboardBoxModel model || model.IsPlaced)
        {
            return;
        }

        var pos = e.GetPosition(BoxesPanel);
        model.X = Math.Max(0, pos.X - 20);
        model.Y = Math.Max(0, pos.Y - 12);
        model.IsPlaced = true;
        BringToFront(model);
        _visibleBoxes.Add(model);
    }

    private void OnLineNameMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not MainViewModel vm)
        {
            return;
        }

        var picker = new LineSelectWindow { Owner = this };
        if (picker.ShowDialog() == true &&
            picker.SelectedLineId is { } lineId &&
            picker.SelectedElectrodeType is { } electrodeType)
        {
            if (vm.SelectedLine.LineId == lineId && vm.SelectedElectrode == electrodeType)
            {
                return;
            }

            if (!SaveLayout(vm)) return;
            vm.SelectLine(lineId, electrodeType);
            ResetLayout();
            RestoreSavedLayout(vm);
        }
    }

    private void OnSidebarToggleClick(object sender, RoutedEventArgs e)
    {
        if (_sidebarExpanded)
        {
            _sidebarExpandedWidth = SidebarContentColumn.ActualWidth;
        }

        _sidebarExpanded = !_sidebarExpanded;
        SidebarContentColumn.MinWidth = _sidebarExpanded ? 160 : 0;
        SidebarContentColumn.Width = new GridLength(_sidebarExpanded ? _sidebarExpandedWidth : 0);
        SidebarContent.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitterColumn.Width = new GridLength(_sidebarExpanded ? 6 : 0);
        SidebarWidthSplitter.Visibility = _sidebarExpanded ? Visibility.Visible : Visibility.Collapsed;
        SidebarToggleButton.ToolTip = _sidebarExpanded ? "박스 목록 접기" : "박스 목록 펼치기";
    }

    private void OnLayoutPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string presetText } ||
            !int.TryParse(presetText, out var preset) || preset is < 1 or > 2 ||
            preset == _selectedLayoutPreset || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!SaveLayout(viewModel))
        {
            LayoutPreset1Button.IsChecked = _selectedLayoutPreset == 1;
            LayoutPreset2Button.IsChecked = _selectedLayoutPreset == 2;
            return;
        }

        _selectedLayoutPreset = preset;
        ResetLayout();
        RestoreSavedLayout(viewModel);
    }

    private string GetLayoutFilePath(MainViewModel viewModel)
    {
        // 기존 단일 배치 파일을 프리셋1로 그대로 사용한다.
        var presetSuffix = _selectedLayoutPreset == 1 ? "" : ".Preset2";
        return Path.Combine(LayoutDirectory,
            $"{viewModel.SelectedLine.LineId}.{viewModel.SelectedElectrode}{presetSuffix}.layout.json");
    }

    private void ResetLayout()
    {
        ClearSelection();
        _visibleBoxes.Clear();
        _nextZIndex = 1;

        for (var i = 0; i < _allBoxes.Count; i++)
        {
            var box = _allBoxes[i];
            box.IsPlaced = false;
            box.X = 20 + (i % 3) * (DefaultCardWidth + 20);
            box.Y = 20 + (i / 3) * (DefaultCardHeight + 20);
            box.ZIndex = 0;

            if (box.Card is FrameworkElement card)
            {
                card.Width = DefaultCardWidth;
                card.Height = DefaultCardHeight;
            }
        }
    }

    private void RestoreSavedLayout(MainViewModel viewModel)
    {
        var layoutFilePath = GetLayoutFilePath(viewModel);
        if (!File.Exists(layoutFilePath))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(layoutFilePath));
            if (state is null)
            {
                return;
            }

            // 기본값은 "전부 안 놓임" — 저장돼있던 Visible 목록에 있는 것만 캔버스에 되돌려놓음.
            foreach (var id in state.Visible)
            {
                var box = _allBoxes.FirstOrDefault(b => b.Id == id);
                if (box is not null && !box.IsPlaced)
                {
                    box.IsPlaced = true;
                    BringToFront(box);
                    _visibleBoxes.Add(box);
                }
            }

            foreach (var box in _allBoxes)
            {
                if (state.X.TryGetValue(box.Id, out var x))
                {
                    box.X = x;
                }

                if (state.Y.TryGetValue(box.Id, out var y))
                {
                    box.Y = y;
                }

                if (box.Card is not FrameworkElement fe)
                {
                    continue;
                }

                if (state.Widths.TryGetValue(box.Id, out var w))
                {
                    fe.Width = w;
                }

                if (state.Heights.TryGetValue(box.Id, out var h))
                {
                    fe.Height = h;
                }
            }
        }
        catch
        {
            // 저장된 레이아웃이 깨졌어도 기본 배치로 계속 — 앱 구동을 막지 않음.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            e.Cancel = !SaveLayout(viewModel);
        }
    }

    private bool SaveLayout(MainViewModel viewModel)
    {
        try
        {
            var layoutFilePath = GetLayoutFilePath(viewModel);
            Directory.CreateDirectory(LayoutDirectory);
            var state = new LayoutState
            {
                Visible = _allBoxes.Where(b => b.IsPlaced).Select(b => b.Id).ToList(),
                X = _allBoxes.ToDictionary(b => b.Id, b => b.X),
                Y = _allBoxes.ToDictionary(b => b.Id, b => b.Y),
                Widths = _allBoxes
                    .Where(b => b.Card is FrameworkElement { Width: var w } && !double.IsNaN(w))
                    .ToDictionary(b => b.Id, b => ((FrameworkElement)b.Card!).Width),
                Heights = _allBoxes
                    .Where(b => b.Card is FrameworkElement { Height: var h } && !double.IsNaN(h))
                    .ToDictionary(b => b.Id, b => ((FrameworkElement)b.Card!).Height)
            };
            File.WriteAllText(layoutFilePath, JsonSerializer.Serialize(state));
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"UI 배치를 저장하지 못했습니다.\n{exception.Message}",
                "배치 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private sealed class DashboardBoxModel : INotifyPropertyChanged
    {
        private bool _isPlaced;
        private double _x;
        private double _y;
        private int _zIndex;

        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public UIElement? Card { get; set; }

        /// <summary>크기 맞춤 기준(마지막 Ctrl+클릭) 표시 배지 — RefreshSelectionVisual이 Visibility 토글.</summary>
        public UIElement? AnchorBadge { get; set; }

        /// <summary>작업 영역에 지금 놓여있는지 — true면 사이드바 카탈로그 행은 흐리게 표시되고 다시 못 눌림.</summary>
        public bool IsPlaced
        {
            get => _isPlaced;
            set => SetField(ref _isPlaced, value);
        }

        /// <summary>캔버스 위 절대 좌표 — Canvas.Left/Top에 바인딩됨.</summary>
        public double X
        {
            get => _x;
            set => SetField(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetField(ref _y, value);
        }

        /// <summary>Canvas 위 쌓임 순서 — 클릭한 박스가 맨 앞으로 오도록 MainWindow.BringToFront가 올림.</summary>
        public int ZIndex
        {
            get => _zIndex;
            set => SetField(ref _zIndex, value);
        }

        private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class LayoutState
    {
        public List<string> Visible { get; set; } = new();
        public Dictionary<string, double> X { get; set; } = new();
        public Dictionary<string, double> Y { get; set; } = new();
        public Dictionary<string, double> Widths { get; set; } = new();
        public Dictionary<string, double> Heights { get; set; } = new();
    }
}
