using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App;

/// <summary>
/// Startup line picker — shown after the splash screen, before MainWindow opens.
/// Everything's on one screen: 동(2동/4동/9동)별로 묶어서 그 동에 실제로 있는 공정만
/// 바로 아래 펼쳐 보여줌(예: 4동은 롤프/코터만, 슬리터 없음) — 클릭 한 번으로 바로 선택.
/// 이전엔 2단계 화면 전환(ListBox 선택 상태 기반)이었는데 재진입 버그가 있어서
/// 화면 전환 자체를 없애고 클릭 이벤트로 직접 네비게이션하는 방식으로 바꿈.
/// </summary>
public partial class LineSelectWindow : Window
{
    public LineId? SelectedLineId { get; private set; }

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

                var row = new Border { Style = (Style)Application.Current.Resources["FlatRow"], Margin = new Thickness(0, 1, 0, 1) };
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(2),
                    Background = (Brush)Application.Current.Resources["AccentBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });
                content.Children.Add(new TextBlock { Text = processLabel, FontSize = 14 });
                row.Child = content;

                row.MouseLeftButtonUp += (_, _) =>
                {
                    SelectedLineId = profile.LineId;
                    DialogResult = true;
                };

                childPanel.Children.Add(row);
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
