using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

public sealed partial class ErrorProofViewModel : ObservableObject, IDisposable
{
    private string? _previousLotId;
    private string? _activeLotId;
    private int _pendingResults;
    private CancellationTokenSource _lifetime = new();

    [ObservableProperty] private bool _isRunning;

    public event Action<string, IReadOnlyList<ErrorProofResult>, string?>? ResultsReady;

    internal static bool IsErrorProofLot(string lotId) => lotId.StartsWith("ERPR", StringComparison.Ordinal);
    internal static bool HasEnded(string? previousLotId, string currentLotId) =>
        !string.IsNullOrWhiteSpace(currentLotId) && currentLotId != "-"
        && previousLotId is not null && IsErrorProofLot(previousLotId)
        && !string.Equals(previousLotId, currentLotId, StringComparison.Ordinal);

    static ErrorProofViewModel()
    {
        Debug.Assert(HasEnded("ERPRA1C105", "202609100005"));
        Debug.Assert(HasEnded("ERPRA1C105", "ERPRA1C106"));
        Debug.Assert(!HasEnded("ERPRA1C105", "ERPRA1C105"));
        Debug.Assert(!HasEnded(null, "202609100005"));
        Debug.Assert(!HasEnded("ERPRA1C105", "-"));
        Debug.Assert(!HasEnded("ERPRA1C105", ""));
    }

    public void ObserveLot(Dong9CoaterPostgresAdapter adapter, string lotId)
    {
        // 빈 값이나 조회 실패를 검사 종료로 취급하지 않는다.
        if (string.IsNullOrWhiteSpace(lotId) || lotId == "-") return;
        if (string.Equals(_previousLotId, lotId, StringComparison.Ordinal)) return;

        var endedLotId = HasEnded(_previousLotId, lotId) ? _previousLotId : null;
        _previousLotId = lotId;
        _activeLotId = IsErrorProofLot(lotId) ? lotId : null;
        if (endedLotId is not null) _pendingResults++;
        IsRunning = _activeLotId is not null || _pendingResults > 0;
        if (endedLotId is not null)
            _ = ShowResultsAfterDelayAsync(adapter, endedLotId, _lifetime.Token);
    }

    private async Task ShowResultsAfterDelayAsync(Dong9CoaterPostgresAdapter adapter, string lotId, CancellationToken ct)
    {
        IReadOnlyList<ErrorProofResult> results = Array.Empty<ErrorProofResult>();
        string? error = null;
        try
        {
            // shortcut: DB 저장 완료 신호 대신 5초 대기한다. 저장 지연이 길어지면 완료 신호 연동이 필요하다.
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            results = await adapter.GetErrorProofResultsAsync(lotId, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            error = $"결과를 조회하지 못했습니다.\n{exception.Message}";
        }

        if (ct.IsCancellationRequested) return;
        _pendingResults--;
        IsRunning = _activeLotId is not null || _pendingResults > 0;
        ResultsReady?.Invoke(lotId, results, error);
    }

    public void Reset()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        _previousLotId = null;
        _activeLotId = null;
        _pendingResults = 0;
        IsRunning = false;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
