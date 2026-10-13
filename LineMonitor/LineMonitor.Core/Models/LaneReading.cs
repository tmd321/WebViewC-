namespace LineMonitor.Core.Models;

public sealed record LaneReading(int Lane, decimal? Tolerance, decimal? Insul, decimal? Overlay)
{
    public decimal? InsulLeft { get; init; }
    public decimal? InsulRight { get; init; }
}

public sealed record LaneMismatch(int Lane, decimal? Left, decimal? Right);

public sealed record FrameSnapshot(
    string LotId,
    string Meter,
    Side Side,
    IReadOnlyList<LaneReading> Lanes,
    DateTime GrabTime);

public sealed record MismatchSnapshot(
    string LotId,
    Side Side,
    IReadOnlyList<LaneMismatch> Lanes,
    DateTime GrabTime);

public sealed record SeriesPoint(DateTime Time, decimal? Value);

public sealed record LaneSeries(int Lane, IReadOnlyList<SeriesPoint> Points);

/// <summary>Id는 DB의 단조증가 PK(public.ng.id) — 증분 폴링 워터마크로 씀(sinceId).</summary>
public sealed record DefectRecord(
    long Id,
    DateTime Time,
    string LotId,
    int Lane,
    string DefectCode,
    string DefectName,
    string? ImagePath,
    decimal? PosX,
    decimal? PosY,
    decimal? SizeX,
    decimal? SizeY);

public sealed record CpkResult(int Lane, CpkCategory Category, double? Value);

public sealed record DefectCodeInfo(int Code, string Name, string NameKo);

public sealed record DefectPivotRow(string Location, IReadOnlyDictionary<string, int> CountsByDefectType);

// --- "Total Server" 레퍼런스 화면(실시간 조회) 전용 모델 ---
// 화면은 레인 셀(폭 방향 N개) 사이사이의 "간격/에지" 값(N+1개, tolerance/cutting 지점)과
// 레인 자체의 Wet/Dry 누적값을 별도 스트립으로 보여준다.

/// <summary>레인 사이 간격(에지) 지점 하나. 3/4 Lane Cut처럼 tolerance 대신 컷폭 라벨이 뜨는 지점도 있음.</summary>
public sealed record EdgeReading(int GapIndex, decimal? Wet, decimal? Dry, string? CutLabel);

/// <summary>레인 셀 하나: Wet/Dry 누적값 + 절연 좌/우 Wet/Dry.</summary>
public sealed record LaneWetDry(
    int Lane,
    decimal? Wet,
    decimal? Dry,
    decimal? InsulWetLeft,
    decimal? InsulWetRight,
    decimal? InsulDryLeft,
    decimal? InsulDryRight);

/// <summary>
/// 스트라이프(간헐) 코팅 한 구간: 코팅부(Coat) + 다음 패턴 전까지의 무지부(Foil, 미코팅 구간).
/// 최대 8패턴까지 반복(2동 슬리터/코터 recipe의 pattern1_.._pattern8_.. 계열 필드에 대응).
/// 마지막 패턴 뒤에는 Foil이 없을 수 있어 FoilLength는 nullable.
/// </summary>
public sealed record PatternSegment(int Index, decimal? CoatLength, decimal? FoilLength);

public sealed record SideMeasurement(
    string LotId,
    string Meter,
    Side Side,
    IReadOnlyList<EdgeReading> Edges,
    IReadOnlyList<LaneWetDry> Lanes,
    decimal? AvrWet,
    decimal? AvrDry,
    IReadOnlyList<PatternSegment> Patterns);
