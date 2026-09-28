using LineMonitor.Core.Models;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Generates plausible fake readings shaped like the real schema (lane count, top/back,
/// value ranges) so the UI can be built and demoed before any DB connection string exists.
/// One instance per line, driven purely by <see cref="LineProfile"/>.
/// </summary>
public sealed class MockLineAdapter : ILineAdapter
{
    private static readonly string[] DefectTypeNames =
    {
        "절연_아일랜드", "표면_경계불균일", "핀홀", "돌기", "분화구", "라인",
        "긁힘", "크랙", "핀홀Lv2", "코팅부주름", "찍힘", "아일랜드", "무지부주름", "열주름"
    };

    private static readonly string[] Locations =
    {
        "Top Wet (Head)", "Back Wet (Head)", "MM Top (Back Outfeed)",
        "MM Back (Back Outfeed)", "오버레이 TOP", "오버레이 BACK"
    };

    private readonly Random _rng;
    private readonly Func<int, string?>? _sampleImageProvider;
    private long _nextDefectId = 1;

    public MockLineAdapter(LineProfile profile, Func<int, string?>? sampleImageProvider = null)
    {
        Profile = profile;
        _rng = new Random(profile.LineId.GetHashCode());
        _sampleImageProvider = sampleImageProvider;
    }

    public LineId LineId => Profile.LineId;
    public LineProfile Profile { get; }

    public Task<FrameSnapshot?> GetLatestFrameAsync(Side side, CancellationToken ct = default)
    {
        var lanes = Enumerable.Range(1, Profile.LaneCount)
            .Select(lane => new LaneReading(
                lane,
                Tolerance: Jitter(0.05m, 0.15m),
                Insul: Jitter(2.30m, 2.65m),
                Overlay: Jitter(140.6m, 141.6m)))
            .ToList();

        var snapshot = new FrameSnapshot(
            LotId: "YCF" + _rng.Next(10000, 99999),
            Meter: (_rng.NextDouble() * 2000).ToString("F2"),
            Side: side,
            Lanes: lanes,
            GrabTime: DateTime.Now);

        return Task.FromResult<FrameSnapshot?>(snapshot);
    }

    public Task<MismatchSnapshot?> GetLatestMismatchAsync(Stage stage, CancellationToken ct = default)
    {
        var (lo, hi) = stage == Stage.Wet ? (-0.4m, 0.3m) : (-0.15m, 0.15m);
        var lanes = Enumerable.Range(1, Profile.LaneCount)
            .Select(lane => new LaneMismatch(lane, Jitter(lo, hi), Jitter(lo, hi)))
            .ToList();

        // Side here just tags which panel produced it; mismatch is inherently a Back-vs-Top diff.
        var snapshot = new MismatchSnapshot("YCF" + _rng.Next(10000, 99999), Side.Back, lanes, DateTime.Now);
        return Task.FromResult<MismatchSnapshot?>(snapshot);
    }

    public Task<SideMeasurement?> GetSideMeasurementAsync(Side side, CancellationToken ct = default)
    {
        var laneCount = Profile.LaneCount;

        // 가끔 한두 지점을 "N Lane Cut" 라벨로 대체 — 레퍼런스 화면의 컷폭 표시 재현.
        var cutGapIndex = laneCount >= 4 && _rng.NextDouble() < 0.6 ? _rng.Next(2, laneCount) : -1;

        var edges = Enumerable.Range(1, laneCount + 1)
            .Select(g => g == cutGapIndex
                ? new EdgeReading(g, null, null, $"{laneCount + 2 - g} Lane Cut")
                : new EdgeReading(g, Jitter(9.5m, 10.9m), Jitter(19.5m, 20.2m), null))
            .ToList();

        var lanes = Enumerable.Range(1, laneCount)
            .Select(lane =>
            {
                var wet = Jitter(160m, 163m);
                var dry = wet + Jitter(-0.2m, 0.2m);
                return new LaneWetDry(
                    lane, wet, dry,
                    InsulWetLeft: Jitter(2.2m, 2.4m), InsulWetRight: Jitter(2.2m, 2.4m),
                    InsulDryLeft: Jitter(2.4m, 2.6m), InsulDryRight: Jitter(2.4m, 2.6m));
            })
            .ToList();

        var avrWet = lanes.Count > 0 ? lanes.Average(l => l.Wet ?? 0) : (decimal?)null;
        var avrDry = lanes.Count > 0 ? lanes.Average(l => l.Dry ?? 0) : (decimal?)null;

        var measurement = new SideMeasurement(
            "YCF" + _rng.Next(10000, 99999),
            (_rng.NextDouble() * 1000).ToString("F2"),
            side, edges, lanes, avrWet, avrDry, BuildPatterns());

        return Task.FromResult<SideMeasurement?>(measurement);
    }

    private List<PatternSegment> BuildPatterns()
    {
        if (!Profile.HasPatternCoating)
        {
            return new List<PatternSegment>();
        }

        var count = _rng.Next(1, LineProfile.MaxPatternCount + 1);
        var patterns = new List<PatternSegment>();
        for (var i = 1; i <= count; i++)
        {
            var foil = i < count ? Jitter(8m, 25m) : (decimal?)null; // 마지막 패턴 뒤엔 포일 없음
            patterns.Add(new PatternSegment(i, Jitter(40m, 120m), foil));
        }

        return patterns;
    }

    public Task<IReadOnlyList<LaneSeries>> GetRecentSeriesAsync(SeriesKind kind, Side side, int take, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var (lo, hi) = kind switch
        {
            SeriesKind.Tolerance => (-0.2m, 0.2m),
            SeriesKind.Insul => (2.2m, 2.7m),
            SeriesKind.Overlay => (140.0m, 142.0m),
            SeriesKind.Mismatch => (-0.15m, 0.15m),
            _ => (0m, 1m)
        };

        var series = Enumerable.Range(1, Profile.LaneCount)
            .Select(lane =>
            {
                var points = Enumerable.Range(0, take)
                    .Select(i => new SeriesPoint(now.AddSeconds(-2 * (take - i)), Jitter(lo, hi)))
                    .ToList();
                return new LaneSeries(lane, points);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<LaneSeries>>(series);
    }

    public Task<IReadOnlyList<CpkResult>> GetCpkAsync(CancellationToken ct = default)
    {
        var categories = Profile.HasTopBack
            ? new[] { CpkCategory.ToleranceTop, CpkCategory.ToleranceBack, CpkCategory.InsulBack, CpkCategory.OverlayBack, CpkCategory.Mismatch }
            : new[] { CpkCategory.ToleranceTop, CpkCategory.Mismatch };

        var results = new List<CpkResult>();
        foreach (var category in categories)
        {
            for (var lane = 1; lane <= Profile.LaneCount; lane++)
            {
                results.Add(new CpkResult(lane, category, _rng.NextDouble() * 15 + 5));
            }
        }

        return Task.FromResult<IReadOnlyList<CpkResult>>(results);
    }

    public Task<IReadOnlyList<DefectRecord>> GetRecentDefectsAsync(int take, long sinceId = 0, CancellationToken ct = default)
    {
        var now = DateTime.Now;

        if (sinceId <= 0)
        {
            // 초기 로드: take개를 그 자리에서 즉시 생성(내림차순 — 최신이 앞)
            var initial = Enumerable.Range(0, take)
                .Select(i => BuildDefect(_nextDefectId++, now.AddSeconds(-30 * i)))
                .ToList();
            return Task.FromResult<IReadOnlyList<DefectRecord>>(initial);
        }

        // 증분 폴링: 매 tick마다 0~2건 정도 새로 "발생"한 걸로 시뮬레이션(오름차순)
        var newCount = _rng.Next(0, 3);
        var records = Enumerable.Range(0, newCount)
            .Select(_ => BuildDefect(_nextDefectId++, now))
            .ToList();

        return Task.FromResult<IReadOnlyList<DefectRecord>>(records);
    }

    private DefectRecord BuildDefect(long id, DateTime time)
    {
        var defectName = DefectTypeNames[_rng.Next(DefectTypeNames.Length)];
        return new DefectRecord(
            Id: id,
            Time: time,
            LotId: "YCF" + _rng.Next(10000, 99999),
            Lane: _rng.Next(1, Profile.LaneCount + 1),
            DefectCode: (100 + _rng.Next(0, 35)).ToString(),
            DefectName: defectName,
            ImagePath: _sampleImageProvider?.Invoke((int)(id % 8)),
            PosX: Jitter(0m, 400m),
            PosY: Jitter(0m, 1800m),
            SizeX: Jitter(0.01m, 0.05m),
            SizeY: Jitter(0.01m, 0.09m));
    }

    public Task<IReadOnlyList<DefectPivotRow>> GetDefectPivotAsync(CancellationToken ct = default)
    {
        var rows = Locations
            .Select(location =>
            {
                var counts = new Dictionary<string, int>();
                // Mostly sparse/zero, occasionally a count — mirrors the mostly-empty pivot in the reference screenshot.
                var hitCount = _rng.Next(0, 3);
                for (var i = 0; i < hitCount; i++)
                {
                    var type = DefectTypeNames[_rng.Next(DefectTypeNames.Length)];
                    counts[type] = counts.GetValueOrDefault(type) + _rng.Next(1, 3);
                }

                return new DefectPivotRow(location, counts);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<DefectPivotRow>>(rows);
    }

    private decimal Jitter(decimal lo, decimal hi)
    {
        var range = hi - lo;
        return lo + (decimal)_rng.NextDouble() * range;
    }
}
