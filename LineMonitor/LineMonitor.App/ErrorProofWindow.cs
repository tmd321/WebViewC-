using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LineMonitor.Core.Models;

namespace LineMonitor.App;

public sealed class ErrorProofWindow : Window
{
    private bool _allowClose;

    public ErrorProofWindow()
    {
        Title = "에러프루프";
        Width = 440;
        Height = 150;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Content = new Border
        {
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = "에러프루프 진행중",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.RoyalBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Closing += OnProgressClosing;
    }

    private void OnProgressClosing(object? sender, CancelEventArgs e) => e.Cancel = !_allowClose;

    public void CloseProgress()
    {
        _allowClose = true;
        Close();
    }

    public static Window CreateResults(string lotId, IReadOnlyList<ErrorProofResult> results, string? error)
    {
        var window = new Window
        {
            Title = $"에러프루프 결과 · {lotId}",
            Width = 850,
            Height = 550,
            MinWidth = 600,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brushes.White
        };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var closeButton = new Button
        {
            Content = "닫기", Width = 100, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0), IsCancel = true
        };
        closeButton.Click += (_, _) => window.Close();
        DockPanel.SetDock(closeButton, Dock.Bottom);
        panel.Children.Add(closeButton);
        var heading = new TextBlock
        {
            Text = error ?? (results.Count == 0 ? $"LOT : {lotId}\n조회된 결과가 없습니다."
                : $"LOT : {lotId}\n검출수량 / 검출되어야 하는 수량"),
            FontSize = 18, TextWrapping = TextWrapping.Wrap,
            Foreground = error is null ? Brushes.Black : Brushes.Red,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        var table = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            FontSize = 18, ItemsSource = BuildResultRows(results)
        };
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "불량항목", Binding = new Binding(nameof(ResultRow.DefectItem)) { Mode = BindingMode.OneWay },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        foreach (var location in new[] { nameof(ResultRow.TH), nameof(ResultRow.BH), nameof(ResultRow.BOT), nameof(ResultRow.BOB) })
        {
            table.Columns.Add(new DataGridTextColumn
            {
                Header = location,
                Binding = new Binding(location) { Mode = BindingMode.OneWay }, Width = 100
            });
        }
        panel.Children.Add(table);
        window.Content = panel;
        return window;
    }

    private sealed record ResultRow(string DefectItem, string TH, string BH, string BOT, string BOB);

    private static IReadOnlyList<ResultRow> BuildResultRows(IReadOnlyList<ErrorProofResult> results) =>
        results.GroupBy(result => result.DefectCode)
            .Select(group => new ResultRow(group.First().DefectItem,
                FormatCount(group, 0), FormatCount(group, 2), FormatCount(group, 3), FormatCount(group, 4)))
            .ToArray();

    private static string FormatCount(IEnumerable<ErrorProofResult> results, int pcIndex)
    {
        var result = results.FirstOrDefault(item => item.PcIndex == pcIndex);
        var detected = (result?.DetectedCount ?? 0).ToString(CultureInfo.InvariantCulture);
        var expected = result?.ExpectedCount?.ToString(CultureInfo.InvariantCulture) ?? "-";
        return $"{detected}/{expected}";
    }

    static ErrorProofWindow()
    {
        var rows = BuildResultRows(new[]
        {
            new ErrorProofResult(100, 0, "핀홀(PINHOLE)", 4, 5),
            new ErrorProofResult(100, 2, "핀홀(PINHOLE)", 0, 5),
            new ErrorProofResult(100, 3, "핀홀(PINHOLE)", 4, null),
            new ErrorProofResult(100, 4, "핀홀(PINHOLE)", 0, 0),
            new ErrorProofResult(103, 4, "크랙(CRACK)", 3, 5)
        });
        Debug.Assert(rows.Count == 2);
        Debug.Assert(rows[0] == new ResultRow("핀홀(PINHOLE)", "4/5", "0/5", "4/-", "0/0"));
        Debug.Assert(rows[1] == new ResultRow("크랙(CRACK)", "0/-", "0/-", "0/-", "3/5"));
        Debug.Assert(BuildResultRows(Array.Empty<ErrorProofResult>()).Count == 0);
    }
}
