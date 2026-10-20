using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
/// moves that one card freely; overlapping boxes are allowed, with no swap-on-drop or auto layout.
/// The canvas starts empty — the left sidebar lists every box type as a catalog the user
/// drags (or double-clicks) onto the canvas; already-placed ones show dimmed and can't be
/// re-added, closing (✕) a card un-dims its row so it can be added back. Position/size/
/// visibility persists per site/process/electrode/preset selection under %LocalAppData%\LineMonitor.
/// </summary>
public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsLayoutEditableProperty = DependencyProperty.Register(
        nameof(IsLayoutEditable), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

    public bool IsLayoutEditable
    {
        get => (bool)GetValue(IsLayoutEditableProperty);
        private set => SetValue(IsLayoutEditableProperty, value);
    }

    public static readonly DependencyProperty IsMeasurementMirroredProperty = DependencyProperty.Register(
        nameof(IsMeasurementMirrored), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsMeasurementMirrored
    {
        get => (bool)GetValue(IsMeasurementMirroredProperty);
        private set => SetValue(IsMeasurementMirroredProperty, value);
    }

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
    private int _selectedLayoutPreset;
    private readonly bool _isSecondaryWindow;
    private bool _isHistoryView;
    private readonly List<MainWindow> _childWindows = new();
    private string? _lastSavedLayout;
    private Rect? _windowBoundsBeforeExpand;
    private Point? _catalogDragStart;
    private DashboardBoxModel? _catalogDragCandidate;
    private Point? _selectionDragStart;

    private readonly HashSet<DashboardBoxModel> _selection = new();
    private DashboardBoxModel? _selectionAnchor;
    private int _nextZIndex = 1;

    public MainWindow() : this(1, false)
    {
    }

    private MainWindow(int layoutPreset, bool isSecondaryWindow)
    {
        _selectedLayoutPreset = layoutPreset;
        _isSecondaryWindow = isSecondaryWindow;
        InitializeComponent();
        WindowTitleText.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });
        if (_isSecondaryWindow)
        {
            NewWindowButton.Visibility = Visibility.Collapsed;
            LineSelectionText.Visibility = Visibility.Collapsed;
            LiveControls.Visibility = Visibility.Collapsed;
            ErrorProofControls.Visibility = Visibility.Collapsed;
            ToolbarLogo.Visibility = Visibility.Collapsed;
            NavigationControls.Margin = new Thickness(0);
        }
        UpdateViewState();
        _visibleBoxes.CollectionChanged += (_, _) =>
            EmptyCanvasHint.Visibility = _visibleBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewMouseDown += OnWindowPreviewMouseDown;
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_selection.Count == 0) return;

        for (var current = e.OriginalSource as DependencyObject; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (_visibleBoxes.Any(box => ReferenceEquals(box.Card, current))) return;
            // 크기 맞춤 버튼은 현재 선택을 사용하므로 해제하지 않는다.
            if (ReferenceEquals(current, MatchWidthButton)
                || ReferenceEquals(current, MatchHeightButton)
                || ReferenceEquals(current, MatchBothButton)) return;
        }

        ClearSelection();
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

    private void OnBoxesSelectionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsLayoutEditable || _isHistoryView) return;
        for (var current = e.OriginalSource as DependencyObject; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ScrollBar
                || _visibleBoxes.Any(box => ReferenceEquals(box.Card, current))) return;
            if (ReferenceEquals(current, BoxesScrollViewer)) break;
        }

        ClearSelection();
        if (!BoxesScrollViewer.CaptureMouse()) return;
        _selectionDragStart = e.GetPosition(BoxesPanel);
        e.Handled = true;
    }

    private void OnBoxesSelectionMouseMove(object sender, MouseEventArgs e)
    {
        if (_selectionDragStart is null) return;
        if (!IsLayoutEditable || e.LeftButton != MouseButtonState.Pressed)
        {
            EndBoxSelection();
            return;
        }
        UpdateBoxSelection(e.GetPosition(BoxesPanel));
        e.Handled = true;
    }

    private void OnBoxesSelectionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionDragStart is null) return;
        if (IsLayoutEditable) UpdateBoxSelection(e.GetPosition(BoxesPanel));
        EndBoxSelection();
        e.Handled = true;
    }

    private void OnBoxesSelectionLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_selectionDragStart is not null && !BoxesScrollViewer.IsMouseCaptured) EndBoxSelection();
    }

    private void UpdateBoxSelection(Point end)
    {
        if (_selectionDragStart is not { } start) return;
        if (SelectionRectangle.Visibility != Visibility.Visible
            && Math.Abs(end.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(end.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // 박스 좌표와 같은 스크롤 콘텐츠 좌표로 판정하고, 사각형만 화면 위에 그린다.
        var bounds = new Rect(start, end);
        var overlayPosition = BoxesPanel.TranslatePoint(bounds.TopLeft, SelectionOverlay);
        Canvas.SetLeft(SelectionRectangle, overlayPosition.X);
        Canvas.SetTop(SelectionRectangle, overlayPosition.Y);
        SelectionRectangle.Width = bounds.Width;
        SelectionRectangle.Height = bounds.Height;
        SelectionRectangle.Visibility = Visibility.Visible;

        _selection.Clear();
        foreach (var model in _visibleBoxes)
        {
            if (model.Card is FrameworkElement card && card.ActualWidth > 0 && card.ActualHeight > 0
                && bounds.IntersectsWith(new Rect(model.X, model.Y, card.ActualWidth, card.ActualHeight)))
            {
                _selection.Add(model);
            }
        }
        _selectionAnchor = _visibleBoxes.LastOrDefault(model => _selection.Contains(model));
        foreach (var model in _visibleBoxes) RefreshSelectionVisual(model);
        UpdateSizeMatchButtonsEnabled();
        Debug.Assert(_selection.All(model => model.Card is FrameworkElement card
            && bounds.IntersectsWith(new Rect(model.X, model.Y, card.ActualWidth, card.ActualHeight))));
    }

    private void EndBoxSelection()
    {
        _selectionDragStart = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        if (BoxesScrollViewer.IsMouseCaptured) BoxesScrollViewer.ReleaseMouseCapture();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLayoutLockClick(object sender, RoutedEventArgs e)
    {
        EndBoxSelection();
        IsLayoutEditable = !IsLayoutEditable;
        ClearSelection();
        _catalogDragStart = null;
        _catalogDragCandidate = null;
        CatalogList.IsEnabled = IsLayoutEditable;
        SizeMatchControls.IsEnabled = IsLayoutEditable;
        LayoutLockButton.Content = IsLayoutEditable ? "UI 잠금" : "UI 잠금 해제";
        LayoutLockButton.Background = IsLayoutEditable ? Brushes.Transparent
            : (Brush)Application.Current.Resources["AccentBrush"];
        LayoutLockButton.Foreground = IsLayoutEditable
            ? (Brush)Application.Current.Resources["TextBrush"] : Brushes.White;
        Debug.Assert(CatalogList.IsEnabled == IsLayoutEditable && SizeMatchControls.IsEnabled == IsLayoutEditable);
    }

    private void OnMirrorMeasurementsClick(object sender, RoutedEventArgs e)
    {
        if (!IsLayoutEditable) return;
        IsMeasurementMirrored = !IsMeasurementMirrored;
    }

    private void ToggleMaximize()
    {
        Rect bounds;
        if (_windowBoundsBeforeExpand is { } previousBounds)
        {
            bounds = previousBounds;
            _windowBoundsBeforeExpand = null;
        }
        else
        {
            bounds = Services.WindowPlacement.GetWorkArea(this);
            _windowBoundsBeforeExpand = WindowState == WindowState.Maximized
                ? RestoreBounds
                : new Rect(Left, Top, ActualWidth, ActualHeight);
        }

        // 실제 최대화 상태에서는 Windows가 이동과 크기 조절을 막으므로 일반 창 크기만 확장한다.
        WindowState = WindowState.Normal;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        Debug.Assert(WindowState == WindowState.Normal && ResizeMode == ResizeMode.CanResize);
    }

    private void OnOpenErrorProofResultsClick(object sender, RoutedEventArgs e)
    {
        const string folderPath = @"D:\Data\ErrorProof\";
        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show(this, $"결과 폴더가 없습니다.\n{folderPath}",
                "에러프루프 결과", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox.Show(this, $"결과 폴더를 열지 못했습니다.\n{exception.Message}",
                "에러프루프 결과", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnStartErrorProofClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !button.IsEnabled || DataContext is not MainViewModel viewModel) return;

        var selectionName = viewModel.SelectionDisplayName;
        button.IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var updatedRows = await viewModel.StartErrorProofAsync(timeout.Token);
            if (updatedRows == 0)
            {
                MessageBox.Show(this, $"{selectionName}\nerror_proofing_signal 테이블에 갱신할 행이 없습니다.",
                    "에러프루프 시작", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, $"{selectionName}\n요청 시간이 초과되었습니다. 재시도하기 전에 DB의 is_start 상태를 확인해 주세요.",
                "에러프루프 시작", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"{selectionName}\n시작 신호 전송 결과를 확인하지 못했습니다.\n{exception.Message}",
                "에러프루프 시작", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;

        BoxesScrollViewer.ApplyTemplate();
        if (BoxesScrollViewer.Template.FindName("PART_VerticalScrollBar", BoxesScrollViewer) is FrameworkElement verticalScrollBar)
        {
            verticalScrollBar.Width = 24;
            verticalScrollBar.MinWidth = 24;
        }

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

    private FrameworkElement BindControl(FrameworkElement control, object source, string path)
    {
        if (control is Controls.SidePanel sidePanel)
        {
            sidePanel.SetBinding(Controls.SidePanel.ShowInsulationProperty,
                new Binding(nameof(MainViewModel.ShowInsulation)) { Source = source });
            sidePanel.SetBinding(Controls.SidePanel.IsMirroredProperty,
                new Binding(nameof(IsMeasurementMirrored)) { Source = this });
        }

        if (control is Controls.MismatchPanel mismatchPanel)
        {
            mismatchPanel.SetBinding(Controls.MismatchPanel.IsCathodeElectrodeProperty,
                new Binding(nameof(MainViewModel.ShowInsulation)) { Source = source });
            mismatchPanel.SetBinding(Controls.MismatchPanel.IsMirroredProperty,
                new Binding(nameof(IsMeasurementMirrored)) { Source = this });
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
            FontSize = 14,
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
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "가로 최대 너비에 맞춤"
        };
        fitWidthButton.Click += (_, _) =>
        {
            var maxWidth = GetMaxCardWidth();
            if (maxWidth > 0) ResizeBox(model, maxWidth, card.Height);
        };

        var fitHeightButton = new Button
        {
            Content = "⇕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "세로 스크롤 없이 내용에 맞춰 최소 높이로"
        };
        fitHeightButton.Click += (_, _) => FitBoxHeight(model);

        var closeButton = new Button
        {
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 15,
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
        titleBar.SetBinding(IsEnabledProperty, new Binding(nameof(IsLayoutEditable)) { Source = this });

        // 제목바 드래그 = 선택된 박스들을 같은 이동량으로 함께 이동.
        // Ctrl+클릭이면 이동 대신 다중 선택 토글(MFC 다이얼로그 에디터처럼) — "크기 맞춤" 버튼들이 이 선택을 씀.
        Point? moveStart = null;
        var moveStartPositions = new Dictionary<DashboardBoxModel, Point>();
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsLayoutEditable) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                ToggleSelection(model);
                e.Handled = true;
                return;
            }

            if (!_selection.Contains(model))
            {
                ClearSelection();
            }

            moveStart = e.GetPosition(null);
            moveStartPositions.Clear();
            foreach (var box in _selection.Count > 0 ? _selection.ToList() : new List<DashboardBoxModel> { model })
            {
                moveStartPositions.Add(box, new Point(box.X, box.Y));
            }
            if (!titleBar.CaptureMouse())
            {
                moveStart = null;
                moveStartPositions.Clear();
            }
            e.Handled = true;
        };
        titleBar.MouseMove += (_, e) =>
        {
            if (!IsLayoutEditable || moveStart is not { } start
                || moveStartPositions.Count == 0 || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pos = e.GetPosition(null);
            // 그룹 전체에 같은 경계 제한을 적용해 왼쪽·위쪽 끝에서도 상대 위치를 유지한다.
            var deltaX = Math.Max(pos.X - start.X, -moveStartPositions.Values.Min(point => point.X));
            var deltaY = Math.Max(pos.Y - start.Y, -moveStartPositions.Values.Min(point => point.Y));
            foreach (var (box, position) in moveStartPositions)
            {
                MoveBox(box, position.X + deltaX, position.Y + deltaY);
                Debug.Assert(Math.Abs(box.X - position.X - deltaX) < 0.001
                    && Math.Abs(box.Y - position.Y - deltaY) < 0.001);
            }
        };
        titleBar.MouseLeftButtonUp += (_, _) =>
        {
            moveStart = null;
            moveStartPositions.Clear();
            titleBar.ReleaseMouseCapture();
        };
        titleBar.LostMouseCapture += (_, _) =>
        {
            moveStart = null;
            moveStartPositions.Clear();
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

        void AttachResizeHandle(FrameworkElement handle, int horizontalDirection, int verticalDirection)
        {
            handle.SetBinding(IsEnabledProperty, new Binding(nameof(IsLayoutEditable)) { Source = this });
            Point? resizeStart = null;
            double resizeStartWidth = 0, resizeStartHeight = 0;
            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (!IsLayoutEditable) return;
                resizeStart = e.GetPosition(null);
                resizeStartWidth = card.Width;
                resizeStartHeight = card.Height;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (resizeStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;

                var pos = e.GetPosition(null);
                var width = horizontalDirection == 0 ? resizeStartWidth
                    : Math.Max(MinCardWidth, resizeStartWidth + horizontalDirection * (pos.X - start.X));
                var height = verticalDirection == 0 ? resizeStartHeight
                    : Math.Max(MinCardHeight, resizeStartHeight + verticalDirection * (pos.Y - start.Y));
                ResizeBox(model, width, height, horizontalDirection < 0, verticalDirection < 0);
            };
            handle.MouseLeftButtonUp += (_, e) =>
            {
                resizeStart = null;
                handle.ReleaseMouseCapture();
                e.Handled = true;
            };
            handle.LostMouseCapture += (_, _) => resizeStart = null;
        }

        var cardGrid = new Grid();
        cardGrid.Children.Add(body);
        foreach (var (horizontalDirection, verticalDirection) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var edge = new Border
            {
                Background = Brushes.Transparent,
                Cursor = horizontalDirection != 0 ? Cursors.SizeWE : Cursors.SizeNS,
                HorizontalAlignment = horizontalDirection < 0 ? HorizontalAlignment.Left
                    : horizontalDirection > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                VerticalAlignment = verticalDirection < 0 ? VerticalAlignment.Top
                    : verticalDirection > 0 ? VerticalAlignment.Bottom : VerticalAlignment.Stretch,
                Width = horizontalDirection != 0 ? 6 : double.NaN,
                Height = verticalDirection != 0 ? 6 : double.NaN,
                Margin = horizontalDirection != 0 ? new Thickness(0, 6, 0, 6) : new Thickness(6, 0, 6, 0)
            };
            AttachResizeHandle(edge, horizontalDirection, verticalDirection);
            cardGrid.Children.Add(edge);
        }
        AttachResizeHandle(grip, 1, 1);
        cardGrid.Children.Add(grip);

        var bottomLeftGrip = new Border
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeNESW,
            ToolTip = "드래그해서 가로·세로 크기 조절"
        };
        AttachResizeHandle(bottomLeftGrip, -1, 1);
        cardGrid.Children.Add(bottomLeftGrip);

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
        card.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (IsLayoutEditable) BringToFront(model);
        };

        return card;
    }

    private void MoveBox(DashboardBoxModel model, double x, double y)
    {
        if (!IsLayoutEditable || model.Card is not FrameworkElement) return;
        model.X = Math.Max(0, x);
        model.Y = Math.Max(0, y);
    }

    private double GetMaxCardWidth()
    {
        // 목록을 접어도 펼쳤을 때의 작업 영역 폭을 기준으로 한다.
        var sidebarReserve = _sidebarExpanded ? 0 : _sidebarExpandedWidth + SidebarWidthSplitter.Width;
        return Math.Max(0, BoxesScrollViewer.ViewportWidth - sidebarReserve
            - BoxesPanel.Margin.Left - BoxesPanel.Margin.Right);
    }

    private void FitBoxHeight(DashboardBoxModel model)
    {
        if (!IsLayoutEditable || model.Card is not FrameworkElement card) return;

        var previousHeight = card.Height;
        double contentHeight;
        try
        {
            // 가로 폭은 유지하고 내부 스크롤 영역까지 전체 내용의 높이를 측정한다.
            card.Height = double.NaN;
            card.Measure(new Size(card.Width, double.PositiveInfinity));
            contentHeight = Math.Ceiling(card.DesiredSize.Height);
        }
        finally
        {
            card.Height = previousHeight;
            card.InvalidateMeasure();
        }

        ResizeBox(model, card.Width, contentHeight);
    }

    private void ResizeBox(DashboardBoxModel model, double width, double height,
        bool fromLeft = false, bool fromTop = false)
    {
        if (!IsLayoutEditable || model.Card is not FrameworkElement card) return;
        var right = model.X + card.Width;
        var bottom = model.Y + card.Height;
        if (width > card.Width)
        {
            var maxWidth = GetMaxCardWidth();
            if (maxWidth > 0) width = Math.Min(width, Math.Max(card.Width, maxWidth));
        }
        if (fromLeft) width = Math.Min(width, right);
        if (fromTop) height = Math.Min(height, bottom);
        if (fromLeft) model.X = right - width;
        card.Width = width;
        if (fromTop) model.Y = bottom - height;
        card.Height = height;
        Debug.Assert(!fromLeft || Math.Abs(model.X + card.Width - right) < 0.001);
        Debug.Assert(!fromTop || Math.Abs(model.Y + card.Height - bottom) < 0.001);
    }

    private void BringToFront(DashboardBoxModel model) => model.ZIndex = _nextZIndex++;

    private void CloseBox(DashboardBoxModel model)
    {
        if (!IsLayoutEditable) return;
        _visibleBoxes.Remove(model);
        model.IsPlaced = false;
        _selection.Remove(model);
        if (_selectionAnchor == model)
        {
            _selectionAnchor = null;
        }

        UpdateSizeMatchButtonsEnabled();
    }

    private void ToggleSelection(DashboardBoxModel model)
    {
        if (!IsLayoutEditable) return;
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
        if (!IsLayoutEditable) return;
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

            if (matchWidth)
            {
                MoveBox(model, anchor.X, model.Y);
            }

            ResizeBox(model, matchWidth ? anchorFe.ActualWidth : fe.Width,
                matchHeight ? anchorFe.ActualHeight : fe.Height);
        }
    }

    private void OnCatalogItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!IsLayoutEditable) return;
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
        if (!IsLayoutEditable) return;
        if (((FrameworkElement)sender).DataContext is not DashboardBoxModel { IsPlaced: false } model)
        {
            return;
        }

        _catalogDragStart = e.GetPosition(null);
        _catalogDragCandidate = model;
    }

    private void OnCatalogRowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsLayoutEditable) return;
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
        e.Effects = IsLayoutEditable && e.Data.GetDataPresent(typeof(DashboardBoxModel))
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (!IsLayoutEditable) return;
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
            foreach (var window in _childWindows.ToList())
            {
                window.Close();
                if (window.IsVisible) return;
            }
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
        if (sender is not Button { Tag: string presetText } ||
            !int.TryParse(presetText, out var preset) || preset is < 1 or > 2 ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!_isHistoryView && _selectedLayoutPreset == preset) return;
        if (!SaveLayout(viewModel)) return;

        _selectedLayoutPreset = preset;
        _isHistoryView = false;
        ResetLayout();
        RestoreSavedLayout(viewModel);
        BoxesScrollViewer.ScrollToTop();
        BoxesScrollViewer.ScrollToLeftEnd();
        UpdateViewState();
    }

    private void OnHistoryInquiryClick(object sender, RoutedEventArgs e)
    {
        if (_isHistoryView || DataContext is not MainViewModel viewModel || !SaveLayout(viewModel)) return;

        _isHistoryView = true;
        ClearSelection();
        UpdateViewState();
    }

    private void UpdateViewState()
    {
        DashboardWorkspace.Visibility = _isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        HistoryInquiryView.Visibility = _isHistoryView ? Visibility.Visible : Visibility.Collapsed;
        MainSummaryBar.Visibility = _isHistoryView || _isSecondaryWindow ? Visibility.Collapsed : Visibility.Visible;
        SizeMatchControls.Visibility = _isHistoryView ? Visibility.Collapsed : Visibility.Visible;
        var screenName = _isHistoryView ? "이력조회" : $"UI 프리셋{_selectedLayoutPreset}";
        SetBinding(TitleProperty, new Binding(nameof(MainViewModel.SelectionDisplayName))
        {
            StringFormat = $"{{0}} · {screenName}"
        });

        foreach (var (button, selected) in new[]
                 {
                     (LayoutPreset1Button, !_isHistoryView && _selectedLayoutPreset == 1),
                     (LayoutPreset2Button, !_isHistoryView && _selectedLayoutPreset == 2),
                     (HistoryInquiryButton, _isHistoryView)
                 })
        {
            button.Background = selected ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.Transparent;
            button.Foreground = selected ? Brushes.White : (Brush)Application.Current.Resources["TextBrush"];
        }
    }

    private void OnNewWindowClick(object sender, RoutedEventArgs e)
    {
        var owner = this;
        while (owner.Owner is MainWindow parent) owner = parent;
        if (owner._childWindows.Count > 0) return;
        if (DataContext is not MainViewModel viewModel || !SaveLayout(viewModel)) return;

        var window = new MainWindow(_selectedLayoutPreset, true)
        {
            Owner = owner,
            DataContext = viewModel,
            Width = ActualWidth,
            Height = ActualHeight,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        window._isHistoryView = _isHistoryView;
        window.UpdateViewState();
        window.SourceInitialized += (_, _) => Services.WindowPlacement.FitToOwnerWorkArea(window, this);
        owner._childWindows.Add(window);
        window.Closed += (_, _) => owner._childWindows.Remove(window);
        window.Show();
    }

    private string GetLayoutFilePath(MainViewModel viewModel, bool mainLayout = false)
    {
        var presetSuffix = mainLayout ? "" : $".Preset{_selectedLayoutPreset}";
        return Path.Combine(LayoutDirectory,
            $"{viewModel.SelectedLine.LineId}.{viewModel.SelectedElectrode}{presetSuffix}.layout.json");
    }

    private void ResetLayout()
    {
        EndBoxSelection();
        IsMeasurementMirrored = false;
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
        try
        {
            var layoutFilePath = GetLayoutFilePath(viewModel);
            if (_selectedLayoutPreset == 1 && !File.Exists(layoutFilePath))
            {
                // 기존 프리셋1은 메인 배치 파일에 저장되어 있어 최초 실행 때만 이어받는다.
                layoutFilePath = GetLayoutFilePath(viewModel, mainLayout: true);
            }
            if (!File.Exists(layoutFilePath)) return;

            var state = JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(layoutFilePath));
            if (state is null)
            {
                return;
            }

            IsMeasurementMirrored = state.IsMeasurementMirrored;

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
        finally
        {
            _lastSavedLayout = JsonSerializer.Serialize(CaptureLayoutState());
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // 소유 창이 닫힐 때도 각 프리셋의 배치를 먼저 저장한다.
            foreach (var window in _childWindows.ToList())
            {
                if (!window.SaveLayout(viewModel))
                {
                    e.Cancel = true;
                    return;
                }
            }
            e.Cancel = !SaveLayout(viewModel);
        }
    }

    private LayoutState CaptureLayoutState() => new()
    {
        IsMeasurementMirrored = IsMeasurementMirrored,
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

    private bool SaveLayout(MainViewModel viewModel)
    {
        // 이력조회에 진입할 때 저장했으므로 숨겨진 배치를 다시 덮어쓰지 않는다.
        if (_isHistoryView) return true;
        try
        {
            var layoutFilePath = GetLayoutFilePath(viewModel);
            var serializedLayout = JsonSerializer.Serialize(CaptureLayoutState());
            // 같은 프리셋을 여러 창에서 열어도 수정하지 않은 창이 최신 배치를 덮어쓰지 않는다.
            if (serializedLayout == _lastSavedLayout && File.Exists(layoutFilePath)) return true;
            Directory.CreateDirectory(LayoutDirectory);
            File.WriteAllText(layoutFilePath, serializedLayout);
            _lastSavedLayout = serializedLayout;
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
        public bool IsMeasurementMirrored { get; set; }
        public List<string> Visible { get; set; } = new();
        public Dictionary<string, double> X { get; set; } = new();
        public Dictionary<string, double> Y { get; set; } = new();
        public Dictionary<string, double> Widths { get; set; } = new();
        public Dictionary<string, double> Heights { get; set; } = new();
    }
}
