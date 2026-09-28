using LineMonitor.Core.Models;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Static metadata describing each line's shape (lane count, sides, display name).
/// 2동/4동/9동 x 슬리터/코터 6개는 실제 SQL 덤프 리뷰 기준. 나머지 3개(2동 롤프,
/// 4동 슬리터, 9동 롤프)는 실물 라인은 있는데 스키마 샘플이 없었던 것들 — 같은 공정의
/// 다른 동 스키마를 참고한 추정치이니 실제 접속하면 레인 수 등 맞는지 확인 필요.
/// Drives dynamic layout so the UI doesn't hardcode per-line lane counts.
/// </summary>
public sealed record LineProfile(
    LineId LineId,
    string DisplayName,
    int LaneCount,
    bool HasTopBack,
    bool HasNativeCpkTable,
    bool HasPatternCoating)
{
    /// <summary>스트라이프 코팅 시 패턴 세그먼트 상한(포일로 구분). 2동 recipe의 pattern1../pattern8.. 계열 필드 기준.</summary>
    public const int MaxPatternCount = 8;

    public static readonly IReadOnlyDictionary<LineId, LineProfile> All = new Dictionary<LineId, LineProfile>
    {
        [LineId.Dong2Slitter] = new(LineId.Dong2Slitter, "2동 슬리터", LaneCount: 11, HasTopBack: true, HasNativeCpkTable: false, HasPatternCoating: true),
        [LineId.Dong2Coater] = new(LineId.Dong2Coater, "2동 코터", LaneCount: 4, HasTopBack: true, HasNativeCpkTable: false, HasPatternCoating: true),
        [LineId.Dong2Rollf] = new(LineId.Dong2Rollf, "2동 롤프", LaneCount: 4, HasTopBack: false, HasNativeCpkTable: false, HasPatternCoating: false),
        [LineId.Dong4Slitter] = new(LineId.Dong4Slitter, "4동 슬리터", LaneCount: 8, HasTopBack: false, HasNativeCpkTable: false, HasPatternCoating: false),
        [LineId.Dong4Rollf] = new(LineId.Dong4Rollf, "4동 롤프", LaneCount: 4, HasTopBack: false, HasNativeCpkTable: false, HasPatternCoating: false),
        [LineId.Dong4Coater] = new(LineId.Dong4Coater, "4동 코터", LaneCount: 8, HasTopBack: true, HasNativeCpkTable: true, HasPatternCoating: false),
        [LineId.Dong9Slitter] = new(LineId.Dong9Slitter, "9동 슬리터", LaneCount: 8, HasTopBack: false, HasNativeCpkTable: false, HasPatternCoating: false),
        [LineId.Dong9Rollf] = new(LineId.Dong9Rollf, "9동 롤프", LaneCount: 8, HasTopBack: false, HasNativeCpkTable: false, HasPatternCoating: false),
        [LineId.Dong9Coater] = new(LineId.Dong9Coater, "9동 코터", LaneCount: 9, HasTopBack: true, HasNativeCpkTable: false, HasPatternCoating: false),
    };
}
