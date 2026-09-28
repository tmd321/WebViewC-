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

    public ObservableCollection<LineProfile> Lines { get; }

    [ObservableProperty]
    private LineProfile _selectedLine;

    [ObservableProperty]
    private bool _isLive = true;

    public LiveOverviewViewModel LiveOverview { get; } = new();
    public DefectHistoryViewModel DefectHistory { get; } = new();
    public TrendCpkViewModel TrendCpk { get; } = new();

    public MainViewModel(LineAdapterFactory factory, LineMonitorOptions options, LineId? initialLineId = null)
    {
        _factory = factory;
        Lines = new ObservableCollection<LineProfile>(LineProfile.All.Values);
        _selectedLine = Lines.First(l => l.LineId == (initialLineId ?? LineId.Dong4Coater));
        _adapter = _factory.Create(_selectedLine.LineId);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, options.PollingIntervalSeconds))
        };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();

        _ = TickAsync();
    }

    partial void OnSelectedLineChanged(LineProfile value)
    {
        _adapter = _factory.Create(value.LineId);
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        if (!IsLive)
        {
            return;
        }

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
    }

    [RelayCommand]
    private void ToggleLive() => IsLive = !IsLive;

    public void Dispose() => _timer.Stop();
}
