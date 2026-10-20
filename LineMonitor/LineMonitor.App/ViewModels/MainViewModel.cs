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
    [NotifyPropertyChangedFor(nameof(ShowInsulation))]
    private ElectrodeType _selectedElectrode;

    public bool ShowInsulation => SelectedElectrode == ElectrodeType.Cathode;

    [ObservableProperty]
    private bool _isLive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatabaseStatusText))]
    private bool? _isDatabaseConnected;

    public string DatabaseStatusText => _adapter is MockLineAdapter ? "DB 미사용" : IsDatabaseConnected switch
    {
        true => "정상 동작 중",
        false => "연결 실패",
        null => "DB 연결 확인 중"
    };

    public string SelectionDisplayName =>
        $"{SelectedLine.DisplayName} · {(SelectedElectrode == ElectrodeType.Cathode ? "양극" : "음극")}";

    public LiveOverviewViewModel LiveOverview { get; } = new();
    public DefectHistoryViewModel DefectHistory { get; } = new();
    public TrendCpkViewModel TrendCpk { get; } = new();

    public MainViewModel(
        LineAdapterFactory factory,
        LineMonitorOptions options,
        LineId? initialLineId = null,
        ElectrodeType initialElectrodeType = ElectrodeType.Cathode)
    {
        _factory = factory;
        Lines = new ObservableCollection<LineProfile>(LineProfile.All.Values);
        _selectedLine = Lines.First(l => l.LineId == (initialLineId ?? LineId.Dong4Coater));
        _selectedElectrode = initialElectrodeType;
        TrendCpk.ShowInsulation = ShowInsulation;
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
        TrendCpk.ShowInsulation = ShowInsulation;
        TrendCpk.TrendGroups.Clear();
        TrendCpk.CpkRows.Clear();
        _adapter = _factory.Create(lineId, electrodeType);
        IsDatabaseConnected = null;
        OnPropertyChanged(nameof(DatabaseStatusText));
        DefectHistory.Reset();
        OnPropertyChanged(nameof(SelectionDisplayName));
        _ = TickAsync();
    }

    public Task<int> StartErrorProofAsync(CancellationToken ct = default) => _adapter.StartErrorProofAsync(ct);

    private async Task TickAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var adapter = _adapter;
            if (adapter is not MockLineAdapter)
            {
                bool connected;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    connected = await adapter.CheckConnectionAsync(timeout.Token);
                }
                catch
                {
                    connected = false;
                }

                if (!ReferenceEquals(adapter, _adapter)) return;
                IsDatabaseConnected = connected;
                if (!connected) return;
            }

            if (!IsLive) return;
            await LiveOverview.RefreshAsync(adapter);
            if (!ReferenceEquals(adapter, _adapter)) return;
            await DefectHistory.RefreshAsync(adapter, LiveOverview.LotId);
            if (!ReferenceEquals(adapter, _adapter)) return;
            await TrendCpk.RefreshAsync(adapter);
        }
        catch
        {
            // 테이블 조회 오류는 연결 실패와 구분하고 다음 tick에서 재시도한다.
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
