namespace LineMonitor.Core.Models;

public enum Side
{
    Top,
    Back
}

/// <summary>
/// Wet(코팅 직후, Head 카메라) vs Dry(건조로 통과 후) 측정 단계.
/// 실측 레퍼런스 화면("Total Server")의 TOP/BACK 패널과 Wet/Dry Mismatch 패널이 이 축으로 나뉨.
/// </summary>
public enum Stage
{
    Wet,
    Dry
}

public enum LineId
{
    Dong2Slitter,
    Dong2Coater,
    Dong2Rollf,
    Dong4Slitter,
    Dong4Rollf,
    Dong4Coater,
    Dong9Slitter,
    Dong9Rollf,
    Dong9Coater
}

public enum SeriesKind
{
    Tolerance,
    Insul,
    Overlay,
    Mismatch
}

public enum CpkCategory
{
    ToleranceTop,
    ToleranceBack,
    InsulTop,
    InsulBack,
    OverlayTop,
    OverlayBack,
    Mismatch
}
