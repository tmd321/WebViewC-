using LineMonitor.Core.Models;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Unifies the six differing DB schemas (2동/4동/9동 x 슬리터/코터) behind one query surface.
/// Each concrete adapter maps its own line's tables/columns into the common models.
/// </summary>
public interface ILineAdapter
{
    LineId LineId { get; }
    LineProfile Profile { get; }

    Task<bool> CheckConnectionAsync(CancellationToken ct = default);

    Task<FrameSnapshot?> GetLatestFrameAsync(Side side, CancellationToken ct = default);
    Task<MismatchSnapshot?> GetLatestMismatchAsync(Stage stage, CancellationToken ct = default);

    /// <summary>"실시간 조회" 레퍼런스 화면(Total Server)의 TOP/BACK 패널 데이터.</summary>
    Task<SideMeasurement?> GetSideMeasurementAsync(Side side, CancellationToken ct = default);
    Task<IReadOnlyList<LaneSeries>> GetRecentSeriesAsync(SeriesKind kind, Side side, int take, CancellationToken ct = default);
    Task<IReadOnlyList<CpkResult>> GetCpkAsync(CancellationToken ct = default);
    /// <summary>
    /// sinceId=0이면 최신 take개(초기 로드, 내림차순). sinceId>0이면 그 id보다 큰 것만
    /// 오름차순으로 반환(증분 폴링 — take는 폭주 시 안전장치용 상한).
    /// </summary>
    Task<IReadOnlyList<DefectRecord>> GetRecentDefectsAsync(int take, long sinceId = 0, CancellationToken ct = default);
    Task<IReadOnlyList<DefectPivotRow>> GetDefectPivotAsync(CancellationToken ct = default);
}
