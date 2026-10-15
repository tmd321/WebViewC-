using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LineMonitor.Core.Configuration;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

/// <summary>
/// Owns the line selector and the polling loop. Swapping SelectedLine rebuilds the
/// ILineAdapter (via LineAdapterFactory, mock-or-real per appsettings.json) and the
/// next tick repopulates every child view model against the new line.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly LineAdapterFactory _factory;
    private readonly DispatcherTimer _timer;
    private ILineAdapter _adapter;
    private bool _isRefreshing;

    public ObservableCollection<LineProfile> Lines { get; }

    [ObservableProperty]
    private LineProfile _selectedLine;

    [ObservableProperty]
    private ElectrodeType _selectedElectrode;

    [ObservableProperty]
    private bool _isLive = true;

    public string SelectionDisplayName =>
        $"{SelectedLine.DisplayName} · {(SelectedElectrode == ElectrodeType.Positive ? "양극" : "음극")}";

    public LiveOverviewViewModel LiveOverview { get; } = new();
    public DefectHistoryViewModel DefectHistory { get; } = new();
    public TrendCpkViewModel TrendCpk { get; } = new();

    public MainViewModel(
        LineAdapterFactory factory,
        LineMonitorOptions options,
        LineId? initialLineId = null,
        ElectrodeType initialElectrodeType = ElectrodeType.Positive)
    {
        _factory = factory;
        Lines = new ObservableCollection<LineProfile>(LineProfile.All.Values);
        _selectedLine = Lines.First(l => l.LineId == (initialLineId ?? LineId.Dong4Coater));
        _selectedElectrode = initialElectrodeType;
        _adapter = _factory.Create(_selectedLine.LineId, _selectedElectrode);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, options.PollingIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();

        _ = TickAsync();
    }

    public void SelectLine(LineId lineId, ElectrodeType electrodeType)
    {
        SelectedLine = Lines.First(line => line.LineId == lineId);
        SelectedElectrode = electrodeType;
        _adapter = _factory.Create(lineId, electrodeType);
        OnPropertyChanged(nameof(SelectionDisplayName));
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        if (!IsLive || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var adapter = _adapter;
            await LiveOverview.RefreshAsync(adapter);
            await DefectHistory.RefreshAsync(adapter);
            await TrendCpk.RefreshAsync(adapter);
        }
        catch
        {
            // 라인 오프라인/쿼리 실패는 다음 tick에서 재시도 — MVP 단계에서는 조용히 무시,
            // phase 3에서 라인별 연결 상태 배지로 노출 예정.
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    [RelayCommand]
    private void ToggleLive() => IsLive = !IsLive;

    public void Dispose() => _timer.Stop();
}
