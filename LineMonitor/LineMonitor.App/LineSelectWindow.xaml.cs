using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App;

/// <summary>
/// Startup line picker — shown after the splash screen, before MainWindow opens.
/// 사이트(동) > 공정 > 극성 순서로 펼쳐 선택한다.
/// </summary>
public partial class LineSelectWindow : Window
{
    public LineId? SelectedLineId { get; private set; }
    public ElectrodeType? SelectedElectrodeType { get; private set; }

    public LineSelectWindow()
    {
        InitializeComponent();
        BuildTree();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 마우스 버튼이 이미 떼진 상태에서 호출되면 여기로 옴 — 무시하고 지나감.
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void BuildTree()
    {
        var byDong = LineProfile.All.Values
            .GroupBy(p => p.DisplayName.Split(' ')[0]);

        foreach (var group in byDong)
        {
            var childPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 4), Visibility = Visibility.Collapsed };

            foreach (var profile in group)
            {
                var processLabel = profile.DisplayName[(group.Key.Length + 1)..];
                var electrodePanel = new StackPanel
                {
                    Margin = new Thickness(20, 0, 0, 4),
                    Visibility = Visibility.Collapsed
                };

                foreach (var (electrodeType, label) in new[]
                         {
                             (ElectrodeType.Positive, "양극"),
                             (ElectrodeType.Negative, "음극")
                         })
                {
                    var electrodeRow = new Border
                    {
                        Style = (Style)Application.Current.Resources["FlatRow"],
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    var electrodeContent = new StackPanel { Orientation = Orientation.Horizontal };
                    electrodeContent.Children.Add(new Border
                    {
                        Width = 8,
                        Height = 8,
                        CornerRadius = new CornerRadius(2),
                        Background = (Brush)Application.Current.Resources["AccentBrush"],
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    });
                    electrodeContent.Children.Add(new TextBlock { Text = label, FontSize = 14 });
                    electrodeRow.Child = electrodeContent;
                    electrodeRow.MouseLeftButtonUp += (_, _) =>
                    {
                        SelectedLineId = profile.LineId;
                        SelectedElectrodeType = electrodeType;
                        DialogResult = true;
                    };
                    electrodePanel.Children.Add(electrodeRow);
                }

                var processArrow = new TextBlock
                {
                    Text = "▸",
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var processContent = new StackPanel { Orientation = Orientation.Horizontal };
                processContent.Children.Add(processArrow);
                processContent.Children.Add(new TextBlock { Text = processLabel, FontSize = 14 });

                var processRow = new Border
                {
                    Style = (Style)Application.Current.Resources["FlatRow"],
                    Margin = new Thickness(0, 1, 0, 1),
                    Child = processContent
                };
                var processExpanded = false;
                processRow.MouseLeftButtonUp += (_, _) =>
                {
                    processExpanded = !processExpanded;
                    electrodePanel.Visibility = processExpanded ? Visibility.Visible : Visibility.Collapsed;
                    processArrow.Text = processExpanded ? "▾" : "▸";
                };

                childPanel.Children.Add(processRow);
                childPanel.Children.Add(electrodePanel);
            }

            // 접었다 폈다 되는 동 헤더 — 화살표 방향으로 상태 표시, 클릭하면 아래 공정 목록 토글.
            var arrow = new TextBlock { Text = "▸", FontSize = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var headerText = new TextBlock { Text = group.Key, FontSize = 13, FontWeight = FontWeights.SemiBold };
            var headerContent = new StackPanel { Orientation = Orientation.Horizontal };
            headerContent.Children.Add(arrow);
            headerContent.Children.Add(headerText);

            var headerRow = new Border
            {
                Style = (Style)Application.Current.Resources["FlatRow"],
                Margin = new Thickness(0, 10, 0, 2),
                Child = headerContent
            };

            var expanded = false;
            headerRow.MouseLeftButtonUp += (_, _) =>
            {
                expanded = !expanded;
                childPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                arrow.Text = expanded ? "▾" : "▸";
            };

            TreePanel.Children.Add(headerRow);
            TreePanel.Children.Add(childPanel);
        }
    }
}
