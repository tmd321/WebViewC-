using LineMonitor.Core.Cpk;
using LineMonitor.Core.Models;
using Npgsql;
using System.Globalization;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Real Npgsql-backed adapter for 9동 코터(OC9). 스키마 출처: 코터/SQL/CreateTableSQL.txt.
///
/// 확인 필요(가정) 항목:
/// - frame/mismatch의 pc_index는 error_proofing 정의를 따라 0=TH(Top Wet), 2=BH(Back Wet),
///   3=BOT(Top Dry), 4=BOB(Back Dry)로 매핑. 실접속 후 기존 화면값과 최종 대조 필요.
/// - public.frame엔 e_N_tolerance 컬럼이 없음(4동과 다름) — lane_N_null_width(포일/무지부 폭)를
///   "Tolerance" 자리에 대신 씀. null_width는 9레인, coat/insul_l/r은 8레인까지만 있음(스키마 자체의
///   비대칭 — 여기서 만든 제약 아님).
/// - public.cpk 테이블이 없음(4동만 있음) — recipe/recipe_insul의 min/max를 스펙한계로 써서
///   CpkCalculator로 직접 계산.
/// - public.ng.defect_name은 "불량 full path"(이미지 경로) 컬럼 — 사람이 읽는 불량명은
///   defect_code의 한글명(컬럼이 있는 경우), 영문명 순서로 가져옴.
/// </summary>
public sealed class Dong9CoaterPostgresAdapter : ILineAdapter
{
    private const int SampleTake = 30;

    private readonly string _connectionString;
    private readonly bool _hasInsulation;

    public Dong9CoaterPostgresAdapter(string connectionString, bool hasInsulation = true)
    {
        _connectionString = connectionString;
        _hasInsulation = hasInsulation;
    }

    public LineId LineId => LineId.Dong9Coater;
    public LineProfile Profile => LineProfile.All[LineId.Dong9Coater];

    public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
        return true;
    }

    private static int FramePcIndexFor(Side side, Stage stage) => (side, stage) switch
    {
        (Side.Top, Stage.Wet) => 0,
        (Side.Back, Stage.Wet) => 2,
        (Side.Top, Stage.Dry) => 3,
        (Side.Back, Stage.Dry) => 4,
        _ => throw new ArgumentOutOfRangeException()
    };

    private static int DryPcIndexFor(Side side) => FramePcIndexFor(side, Stage.Dry);
    private static int MismatchPcIndexFor(Stage stage) => stage == Stage.Wet ? 2 : 4;

    public Task<FrameSnapshot?> GetLatestFrameAsync(Side side, CancellationToken ct = default) =>
        GetLatestFrameAsync(side, Stage.Dry, ct);

    private async Task<FrameSnapshot?> GetLatestFrameAsync(Side side, Stage stage, CancellationToken ct)
    {
        var insulColumns = _hasInsulation
            ? ", " + string.Join(", ", Enumerable.Range(1, 8).SelectMany(lane => new[]
                { $"lane_{lane}_insul_l_width", $"lane_{lane}_insul_r_width" }))
            : "";
        var sql = $"""
            select lot_id, meter, grap_time, lane_count,
                   lane_1_null_width, lane_2_null_width, lane_3_null_width, lane_4_null_width,
                   lane_5_null_width, lane_6_null_width, lane_7_null_width, lane_8_null_width, lane_9_null_width,
                   lane_1_coat_width, lane_2_coat_width, lane_3_coat_width, lane_4_coat_width,
                   lane_5_coat_width, lane_6_coat_width, lane_7_coat_width, lane_8_coat_width
                   {insulColumns}
            from public.frame
            where pc_index = @pcIndex
            order by id desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pcIndex", FramePcIndexFor(side, stage));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var lanes = new List<LaneReading>();
        for (var lane = 1; lane <= 9; lane++)
        {
            var nullWidth = ReadDecimal(reader, $"lane_{lane}_null_width");
            decimal? insul = null;
            decimal? insulLeft = null;
            decimal? insulRight = null;
            decimal? coat = null;
            if (lane <= 8)
            {
                if (_hasInsulation)
                {
                    insulLeft = ReadDecimal(reader, $"lane_{lane}_insul_l_width");
                    insulRight = ReadDecimal(reader, $"lane_{lane}_insul_r_width");
                    insul = Average(insulLeft, insulRight);
                }
                coat = ReadDecimal(reader, $"lane_{lane}_coat_width");
            }

            lanes.Add(new LaneReading(lane, Tolerance: nullWidth, Insul: insul, Overlay: coat)
            {
                InsulLeft = insulLeft,
                InsulRight = insulRight
            });
        }

        return new FrameSnapshot(
            LotId: reader.GetString(reader.GetOrdinal("lot_id")),
            Meter: reader.IsDBNull(reader.GetOrdinal("meter")) ? "" : reader.GetString(reader.GetOrdinal("meter")),
            Side: side,
            Lanes: lanes,
            GrabTime: ParseGrapTime(reader, "grap_time"))
        {
            LaneCount = reader.IsDBNull(reader.GetOrdinal("lane_count"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("lane_count"))
        };
    }

    public async Task<MismatchSnapshot?> GetLatestMismatchAsync(Stage stage, CancellationToken ct = default)
    {
        const string sql = """
            select lot_id, grap_time, lane_count,
                   lane_1_l_mismatch, lane_1_r_mismatch, lane_2_l_mismatch, lane_2_r_mismatch,
                   lane_3_l_mismatch, lane_3_r_mismatch, lane_4_l_mismatch, lane_4_r_mismatch,
                   lane_5_l_mismatch, lane_5_r_mismatch, lane_6_l_mismatch, lane_6_r_mismatch,
                   lane_7_l_mismatch, lane_7_r_mismatch, lane_8_l_mismatch, lane_8_r_mismatch
            from public.mismatch
            where pc_index = @pcIndex
            order by id desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pcIndex", MismatchPcIndexFor(stage));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var laneCountOrdinal = reader.GetOrdinal("lane_count");
        var laneCount = Math.Clamp(reader.IsDBNull(laneCountOrdinal) ? 8 : reader.GetInt32(laneCountOrdinal), 1, 8);
        var lanes = new List<LaneMismatch>();
        for (var lane = 1; lane <= laneCount; lane++)
        {
            lanes.Add(new LaneMismatch(
                lane,
                ReadDecimal(reader, $"lane_{lane}_l_mismatch"),
                ReadDecimal(reader, $"lane_{lane}_r_mismatch")));
        }

        return new MismatchSnapshot(
            reader.GetString(reader.GetOrdinal("lot_id")),
            Side.Back,
            lanes,
            ParseGrapTime(reader, "grap_time"));
    }

    public async Task<SideMeasurement?> GetSideMeasurementAsync(Side side, CancellationToken ct = default)
    {
        var wetFrame = await GetLatestFrameAsync(side, Stage.Wet, ct);
        var dryFrame = await GetLatestFrameAsync(side, Stage.Dry, ct);
        if (wetFrame is null && dryFrame is null)
        {
            return null;
        }

        var wetByLane = wetFrame?.Lanes.ToDictionary(lane => lane.Lane) ?? new Dictionary<int, LaneReading>();
        var dryByLane = dryFrame?.Lanes.ToDictionary(lane => lane.Lane) ?? new Dictionary<int, LaneReading>();
        var displayFrame = dryFrame ?? wetFrame!;
        var laneCount = Math.Clamp(displayFrame.LaneCount ?? 8, 1, 8);

        var edges = Enumerable.Range(1, laneCount + 1)
            .Select(lane => new EdgeReading(
                lane,
                Wet: wetByLane.GetValueOrDefault(lane)?.Tolerance,
                Dry: dryByLane.GetValueOrDefault(lane)?.Tolerance,
                CutLabel: null))
            .ToList();

        var lanes = Enumerable.Range(1, laneCount)
            .Select(lane => new LaneWetDry(
                lane,
                Wet: wetByLane.GetValueOrDefault(lane)?.Overlay,
                Dry: dryByLane.GetValueOrDefault(lane)?.Overlay,
                InsulWetLeft: wetByLane.GetValueOrDefault(lane)?.InsulLeft,
                InsulWetRight: wetByLane.GetValueOrDefault(lane)?.InsulRight,
                InsulDryLeft: dryByLane.GetValueOrDefault(lane)?.InsulLeft,
                InsulDryRight: dryByLane.GetValueOrDefault(lane)?.InsulRight))
            .ToList();

        var avrWet = AverageValues(lanes.Select(lane => lane.Wet));
        var avrDry = AverageValues(lanes.Select(lane => lane.Dry));

        // 9동 코터는 스트라이프 패턴 코팅 없음(HasPatternCoating:false) — 빈 목록.
        return new SideMeasurement(displayFrame.LotId, displayFrame.Meter, side, edges, lanes, avrWet, avrDry, Array.Empty<PatternSegment>());
    }

    public async Task<IReadOnlyList<LaneSeries>> GetRecentSeriesAsync(SeriesKind kind, Side side, int take, CancellationToken ct = default)
    {
        return kind switch
        {
            SeriesKind.Tolerance => await GetFrameColumnSeriesAsync("lane_{0}_null_width", 9, DryPcIndexFor(side), take, ct),
            SeriesKind.Overlay => await GetFrameColumnSeriesAsync("lane_{0}_coat_width", 8, DryPcIndexFor(side), take, ct),
            SeriesKind.Insul => _hasInsulation
                ? await GetInsulSeriesAsync(DryPcIndexFor(side), take, ct)
                : Array.Empty<LaneSeries>(),
            SeriesKind.Mismatch => await GetMismatchSeriesAsync(take, ct),
            _ => Array.Empty<LaneSeries>()
        };
    }

    private async Task<IReadOnlyList<LaneSeries>> GetFrameColumnSeriesAsync(string columnTemplate, int laneCount, int pcIndex, int take, CancellationToken ct)
    {
        var cols = string.Join(",", Enumerable.Range(1, laneCount).Select(l => string.Format(columnTemplate, l)));
        var sql = $"""
            select grap_time, {cols}
            from public.frame
            where pc_index = @pcIndex
            order by id desc
            limit @take
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pcIndex", pcIndex);
        cmd.Parameters.AddWithValue("take", take);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byLane = Enumerable.Range(1, laneCount).ToDictionary(l => l, _ => new List<SeriesPoint>());
        while (await reader.ReadAsync(ct))
        {
            var time = ParseGrapTime(reader, "grap_time");
            for (var lane = 1; lane <= laneCount; lane++)
            {
                byLane[lane].Add(new SeriesPoint(time, ReadDecimal(reader, string.Format(columnTemplate, lane))));
            }
        }

        return byLane.Select(kv => new LaneSeries(kv.Key, kv.Value)).ToList();
    }

    private async Task<IReadOnlyList<LaneSeries>> GetInsulSeriesAsync(int pcIndex, int take, CancellationToken ct)
    {
        var cols = string.Join(",", Enumerable.Range(1, 8).SelectMany(l => new[] { $"lane_{l}_insul_l_width", $"lane_{l}_insul_r_width" }));
        var sql = $"""
            select grap_time, {cols}
            from public.frame
            where pc_index = @pcIndex
            order by id desc
            limit @take
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("pcIndex", pcIndex);
        cmd.Parameters.AddWithValue("take", take);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byLane = Enumerable.Range(1, 8).ToDictionary(l => l, _ => new List<SeriesPoint>());
        while (await reader.ReadAsync(ct))
        {
            var time = ParseGrapTime(reader, "grap_time");
            for (var lane = 1; lane <= 8; lane++)
            {
                var left = ReadDecimal(reader, $"lane_{lane}_insul_l_width");
                var right = ReadDecimal(reader, $"lane_{lane}_insul_r_width");
                byLane[lane].Add(new SeriesPoint(time, Average(left, right)));
            }
        }

        return byLane.Select(kv => new LaneSeries(kv.Key, kv.Value)).ToList();
    }

    private async Task<IReadOnlyList<LaneSeries>> GetMismatchSeriesAsync(int take, CancellationToken ct)
    {
        var cols = string.Join(",", Enumerable.Range(1, 8).SelectMany(l => new[] { $"lane_{l}_l_mismatch", $"lane_{l}_r_mismatch" }));
        var sql = $"""
            select grap_time, {cols}
            from public.mismatch
            order by id desc
            limit @take
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("take", take);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byLane = Enumerable.Range(1, 8).ToDictionary(l => l, _ => new List<SeriesPoint>());
        while (await reader.ReadAsync(ct))
        {
            var time = ParseGrapTime(reader, "grap_time");
            for (var lane = 1; lane <= 8; lane++)
            {
                var left = ReadDecimal(reader, $"lane_{lane}_l_mismatch");
                var right = ReadDecimal(reader, $"lane_{lane}_r_mismatch");
                byLane[lane].Add(new SeriesPoint(time, Average(left, right)));
            }
        }

        return byLane.Select(kv => new LaneSeries(kv.Key, kv.Value)).ToList();
    }

    public async Task<IReadOnlyList<CpkResult>> GetCpkAsync(CancellationToken ct = default)
    {
        var spec = await GetLatestRecipeSpecAsync(ct);
        var results = new List<CpkResult>();

        foreach (var side in new[] { Side.Top, Side.Back })
        {
            var category = side == Side.Top ? CpkCategory.ToleranceTop : CpkCategory.ToleranceBack;
            var series = await GetFrameColumnSeriesAsync("lane_{0}_null_width", 9, DryPcIndexFor(side), SampleTake, ct);
            foreach (var s in series)
            {
                var (lsl, usl) = spec.NullWidthRange(s.Lane);
                results.Add(new CpkResult(s.Lane, category, CpkCalculator.Calculate(Values(s), lsl, usl)));
            }
        }

        if (_hasInsulation)
        {
            var insulSeries = await GetInsulSeriesAsync(DryPcIndexFor(Side.Back), SampleTake, ct);
            foreach (var s in insulSeries)
            {
                results.Add(new CpkResult(s.Lane, CpkCategory.InsulBack, CpkCalculator.Calculate(Values(s), spec.InsulLsl, spec.InsulUsl)));
            }
        }

        var overlaySeries = await GetFrameColumnSeriesAsync("lane_{0}_coat_width", 8, DryPcIndexFor(Side.Top), SampleTake, ct);
        foreach (var s in overlaySeries)
        {
            var (lsl, usl) = spec.CoatWidthRange(s.Lane);
            results.Add(new CpkResult(s.Lane, CpkCategory.OverlayTop, CpkCalculator.Calculate(Values(s), lsl, usl)));
        }

        var mismatchSeries = await GetMismatchSeriesAsync(SampleTake, ct);
        foreach (var s in mismatchSeries)
        {
            results.Add(new CpkResult(s.Lane, CpkCategory.Mismatch, CpkCalculator.Calculate(Values(s), spec.MismatchLsl, spec.MismatchUsl)));
        }

        return results;
    }

    private static List<decimal> Values(LaneSeries series) =>
        series.Points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).ToList();

    private sealed record RecipeSpec(
        Dictionary<int, (decimal? Lsl, decimal? Usl)> NullWidthByLane,
        Dictionary<int, (decimal? Lsl, decimal? Usl)> CoatWidthByLane,
        decimal? InsulLsl,
        decimal? InsulUsl,
        decimal? MismatchLsl,
        decimal? MismatchUsl)
    {
        public (decimal?, decimal?) NullWidthRange(int lane) =>
            NullWidthByLane.TryGetValue(lane, out var r) ? r : (null, null);

        public (decimal?, decimal?) CoatWidthRange(int lane) =>
            CoatWidthByLane.TryGetValue(lane, out var r) ? r : (null, null);
    }

    private async Task<RecipeSpec> GetLatestRecipeSpecAsync(CancellationToken ct)
    {
        var nullWidth = new Dictionary<int, (decimal?, decimal?)>();
        var coatWidth = new Dictionary<int, (decimal?, decimal?)>();
        decimal? mismatchLsl = null;
        decimal? mismatchUsl = null;

        var nullCols = string.Join(",", Enumerable.Range(1, 9).SelectMany(l => new[] { $"min_null_width_lane_{l}", $"max_null_width_lane_{l}" }));
        var coatCols = string.Join(",", Enumerable.Range(1, 8).SelectMany(l => new[] { $"min_coat_width_lane_{l}", $"max_coat_width_lane_{l}" }));
        var recipeSql = $"""
            select {nullCols}, {coatCols}, mismatch_data_min, mismatch_data_max
            from public.recipe
            order by id desc
            limit 1
            """;

        await using (var conn = new NpgsqlConnection(_connectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(recipeSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                for (var lane = 1; lane <= 9; lane++)
                {
                    nullWidth[lane] = (ReadDecimal(reader, $"min_null_width_lane_{lane}"), ReadDecimal(reader, $"max_null_width_lane_{lane}"));
                }

                for (var lane = 1; lane <= 8; lane++)
                {
                    coatWidth[lane] = (ReadDecimal(reader, $"min_coat_width_lane_{lane}"), ReadDecimal(reader, $"max_coat_width_lane_{lane}"));
                }

                mismatchLsl = ReadDecimal(reader, "mismatch_data_min");
                mismatchUsl = ReadDecimal(reader, "mismatch_data_max");
            }
        }

        decimal? insulLsl = null;
        decimal? insulUsl = null;
        const string insulSql = """
            select min_insul_width, max_insul_width
            from public.recipe_insul
            order by id desc
            limit 1
            """;

        if (_hasInsulation)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(insulSql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                insulLsl = ReadDecimal(reader, "min_insul_width");
                insulUsl = ReadDecimal(reader, "max_insul_width");
            }
        }

        return new RecipeSpec(nullWidth, coatWidth, insulLsl, insulUsl, mismatchLsl, mismatchUsl);
    }

    public async Task<IReadOnlyList<DefectRecord>> GetRecentDefectsAsync(int take, long sinceId = 0, CancellationToken ct = default)
    {
        var order = sinceId > 0 ? "asc" : "desc";
        var sql = $"""
            with latest_lot as (
                select coalesce(
                    (select lot_id from public.frame where nullif(trim(lot_id), '') is not null order by id desc limit 1),
                    (select lot_id from public.ng where nullif(trim(lot_id), '') is not null order by id desc limit 1)
                ) as lot_id
            )
            select n.id, n.defect_time, n.lot_id, n.lane, n.pc_index, n.defect_type_code,
                   coalesce(d.defect_type_name, '') as defect_type_name,
                   coalesce(nullif(to_jsonb(d)->>'defect_type_name_ko', ''), nullif(d.defect_type_name, ''),
                            '코드 ' || n.defect_type_code::text, '미분류') as defect_type_name_ko,
                   n.pos_x, n.pos_y, n.pos_full_x, n.pos_full_y, n.size_x, n.size_y, n.defect_name
            from public.ng n
            join latest_lot l on l.lot_id = n.lot_id
            left join public.defect_code d on d.defect_type_code = n.defect_type_code
            where n.id > @sinceId
            order by n.id {order}
            limit @take
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("take", take);
        cmd.Parameters.AddWithValue("sinceId", sinceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var records = new List<DefectRecord>();
        while (await reader.ReadAsync(ct))
        {
            var pcIndex = reader.IsDBNull(reader.GetOrdinal("pc_index"))
                ? (int?)null : reader.GetInt32(reader.GetOrdinal("pc_index"));
            records.Add(new DefectRecord(
                Id: reader.GetInt32(reader.GetOrdinal("id")),
                Time: ParseGrapTime(reader, "defect_time"),
                LotId: reader.GetString(reader.GetOrdinal("lot_id")),
                Lane: reader.IsDBNull(reader.GetOrdinal("lane")) ? 0 : reader.GetInt32(reader.GetOrdinal("lane")),
                DefectCode: reader.IsDBNull(reader.GetOrdinal("defect_type_code")) ? "" : reader.GetInt32(reader.GetOrdinal("defect_type_code")).ToString(),
                DefectName: reader.IsDBNull(reader.GetOrdinal("defect_type_name_ko")) ? "" : reader.GetString(reader.GetOrdinal("defect_type_name_ko")),
                ImagePath: reader.IsDBNull(reader.GetOrdinal("defect_name")) ? null : reader.GetString(reader.GetOrdinal("defect_name")),
                PosX: ReadDecimal(reader, "pos_x"),
                PosY: ReadDecimal(reader, "pos_y"),
                SizeX: ReadDecimal(reader, "size_x"),
                SizeY: ReadDecimal(reader, "size_y"))
            {
                PcIndex = pcIndex,
                Location = DefectLocationFor(pcIndex),
                DefectTypeName = reader.GetString(reader.GetOrdinal("defect_type_name")),
                PosFullX = ReadDecimal(reader, "pos_full_x"),
                PosFullY = ReadDecimal(reader, "pos_full_y")
            });
        }

        return records;
    }

    public async Task<IReadOnlyList<DefectPivotRow>> GetDefectPivotAsync(CancellationToken ct = default)
    {
        const string sql = """
            with latest_lot as (
                select coalesce(
                    (select lot_id from public.frame where nullif(trim(lot_id), '') is not null order by id desc limit 1),
                    (select lot_id from public.ng where nullif(trim(lot_id), '') is not null order by id desc limit 1)
                ) as lot_id
            )
            select l.lot_id, n.pc_index,
                   coalesce(nullif(to_jsonb(d)->>'defect_type_name_ko', ''), nullif(d.defect_type_name, ''),
                            '코드 ' || n.defect_type_code::text, '미분류') as defect_type_name,
                   count(n.id) as cnt
            from latest_lot l
            left join public.ng n on n.lot_id = l.lot_id
            left join public.defect_code d on d.defect_type_code = n.defect_type_code
            group by 1, 2, 3;

            select coalesce(nullif(to_jsonb(d)->>'defect_type_name_ko', ''), nullif(d.defect_type_name, ''),
                            '코드 ' || d.defect_type_code::text, '미분류')
            from public.defect_code d
            order by d.defect_type_code;
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var countsByLocation = new[] { 0, 2, 3, 4 }
            .ToDictionary(pcIndex => DefectLocationFor(pcIndex), _ => new Dictionary<string, int>());
        var lotId = "-";
        while (await reader.ReadAsync(ct))
        {
            lotId = reader.IsDBNull(0) ? "-" : reader.GetString(0);
            var count = checked((int)reader.GetInt64(3));
            if (count == 0) continue;
            var pcIndex = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
            var location = DefectLocationFor(pcIndex);
            var name = reader.GetString(2);
            if (!countsByLocation.TryGetValue(location, out var counts))
            {
                counts = new Dictionary<string, int>();
                countsByLocation.Add(location, counts);
            }
            counts[name] = counts.GetValueOrDefault(name) + count;
        }

        var types = countsByLocation.Values.SelectMany(counts => counts.Keys).Distinct().ToList();
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (!types.Contains(name)) types.Add(name);
            }
        }
        foreach (var counts in countsByLocation.Values)
        {
            foreach (var type in types) counts.TryAdd(type, 0);
        }

        return countsByLocation.Select(pair =>
            new DefectPivotRow(pair.Key, pair.Value) { LotId = lotId }).ToList();
    }

    private static string DefectLocationFor(int? pcIndex) => pcIndex switch
    {
        0 => "MM Top (Top Head)",
        2 => "MM Back (Back Head)",
        3 => "MM Top (Back Outfeed)",
        4 => "MM Back (Back Outfeed)",
        null => "개소 미지정",
        _ => $"PC {pcIndex}"
    };

    private static decimal? ReadDecimal(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var raw = reader.GetValue(ordinal);
        return raw switch
        {
            decimal d => d,
            string s when decimal.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? Average(decimal? a, decimal? b)
    {
        if (a is null && b is null) return null;
        if (a is null) return b;
        if (b is null) return a;
        return (a.Value + b.Value) / 2;
    }

    private static decimal? AverageValues(IEnumerable<decimal?> values)
    {
        var actual = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return actual.Count == 0 ? null : actual.Average();
    }

    private static DateTime ParseGrapTime(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return DateTime.MinValue;
        }

        var raw = reader.GetString(ordinal);
        if (DateTime.TryParseExact(raw, "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var grabTime)) return grabTime;
        return DateTime.TryParse(raw, out var parsed) ? parsed : DateTime.MinValue;
    }
}
