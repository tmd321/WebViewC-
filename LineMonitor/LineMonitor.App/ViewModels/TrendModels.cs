using System.Collections.ObjectModel;
using LiveChartsCore;

namespace LineMonitor.App.ViewModels;

/// <summary>One mini trend chart card (e.g. "Tolerance Back") holding one series per lane.</summary>
public sealed class TrendSeriesGroup
{
    public required string Title { get; init; }
    public ObservableCollection<ISeries> Series { get; } = new();
}

public sealed class CpkCell
{
    public required int Lane { get; init; }
    public double? Value { get; init; }
}

/// <summary>One CPK summary card (e.g. "Tolerance CPK Top") holding one cell per lane.</summary>
public sealed class CpkCategoryRow
{
    public required string Title { get; init; }
    public ObservableCollection<CpkCell> Cells { get; } = new();
}
