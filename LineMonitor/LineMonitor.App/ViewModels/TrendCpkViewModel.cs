using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;
using LiveChartsCore.SkiaSharpView;

namespace LineMonitor.App.ViewModels;

/// <summary>
/// Backs the shared "mini trend charts + CPK grid" block that appears at the bottom of
/// both reference screens (실시간 조회 / 실시간 불량정보) — one instance, reused by both.
/// </summary>
public sealed partial class TrendCpkViewModel : ObservableObject
{
    public ObservableCollection<TrendSeriesGroup> TrendGroups { get; } = new();
    public ObservableCollection<CpkCategoryRow> CpkRows { get; } = new();
    public bool ShowInsulation { get; set; } = true;

    public async Task RefreshAsync(ILineAdapter adapter, CancellationToken ct = default)
    {
        var showInsulation = ShowInsulation;
        var kinds = new (SeriesKind Kind, Side Side, string Title)[]
        {
            (SeriesKind.Tolerance, Side.Top, "Tolerance Top"),
            (SeriesKind.Tolerance, Side.Back, "Tolerance Back"),
            (SeriesKind.Insul, Side.Back, "Insul Back"),
            (SeriesKind.Overlay, Side.Back, "Overlay Back"),
            (SeriesKind.Mismatch, Side.Back, "Mismatch"),
        };

        var newGroups = new List<TrendSeriesGroup>();
        foreach (var (kind, side, title) in kinds)
        {
            if (kind == SeriesKind.Insul && !showInsulation)
            {
                continue;
            }

            var laneSeries = await adapter.GetRecentSeriesAsync(kind, side, 60, ct);
            var group = new TrendSeriesGroup { Title = title };
            foreach (var ls in laneSeries)
            {
                var values = ls.Points
                    .Select(p => p.Value.HasValue ? (double)p.Value.Value : double.NaN)
                    .ToArray();

                group.Series.Add(new LineSeries<double>
                {
                    Name = $"{ls.Lane} Lane",
                    Values = values,
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0
                });
            }

            newGroups.Add(group);
        }

        if (showInsulation != ShowInsulation)
        {
            return;
        }

        var cpk = await adapter.GetCpkAsync(ct);
        var newRows = cpk
            .Where(c => showInsulation || (c.Category != CpkCategory.InsulTop && c.Category != CpkCategory.InsulBack))
            .GroupBy(c => c.Category)
            .Select(g =>
            {
                var row = new CpkCategoryRow { Title = FormatCategory(g.Key) };
                foreach (var cell in g.OrderBy(c => c.Lane))
                {
                    row.Cells.Add(new CpkCell { Lane = cell.Lane, Value = cell.Value });
                }

                return row;
            })
            .ToList();

        if (showInsulation != ShowInsulation)
        {
            return;
        }

        TrendGroups.Clear();
        foreach (var g in newGroups) TrendGroups.Add(g);

        CpkRows.Clear();
        foreach (var r in newRows) CpkRows.Add(r);
    }

    private static string FormatCategory(CpkCategory category) => category switch
    {
        CpkCategory.ToleranceTop => "Tolerance CPK Top",
        CpkCategory.ToleranceBack => "Tolerance CPK Back",
        CpkCategory.InsulTop => "Insul CPK Top",
        CpkCategory.InsulBack => "Insul CPK Back",
        CpkCategory.OverlayTop => "Overlay CPK Top",
        CpkCategory.OverlayBack => "Overlay CPK Back",
        CpkCategory.Mismatch => "Mismatch CPK",
        _ => category.ToString()
    };
}
