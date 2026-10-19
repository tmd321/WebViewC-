using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

/// <summary>
/// Backs the "실시간 조회" screen, modeled after the real "Total Server" HMI reference:
/// TOP/BACK panels (레인 사이 간격 tolerance + 레인별 Wet/Dry 누적값) and Wet/Dry Mismatch bars.
/// </summary>
public sealed partial class LiveOverviewViewModel : ObservableObject
{
    [ObservableProperty] private string _lotId = "-";
    [ObservableProperty] private string _meter = "-";

    [ObservableProperty] private SideMeasurement? _topMeasurement;
    [ObservableProperty] private SideMeasurement? _backMeasurement;

    public ObservableCollection<LaneMismatch> WetMismatchLanes { get; } = new();
    public ObservableCollection<LaneMismatch> DryMismatchLanes { get; } = new();

    public async Task RefreshAsync(ILineAdapter adapter, CancellationToken ct = default)
    {
        var top = await adapter.GetSideMeasurementAsync(Side.Top, ct);
        var back = await adapter.GetSideMeasurementAsync(Side.Back, ct);
        var wetMismatch = await adapter.GetLatestMismatchAsync(Stage.Wet, ct);
        var dryMismatch = await adapter.GetLatestMismatchAsync(Stage.Dry, ct);

        if (top is not null)
        {
            TopMeasurement = top;
            LotId = top.LotId;
            Meter = top.Meter;
        }

        if (back is not null)
        {
            BackMeasurement = back;
        }

        if (wetMismatch is not null)
        {
            ReplaceAll(WetMismatchLanes, wetMismatch.Lanes);
        }

        if (dryMismatch is not null)
        {
            ReplaceAll(DryMismatchLanes, dryMismatch.Lanes);
        }
    }

    private static void ReplaceAll<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
