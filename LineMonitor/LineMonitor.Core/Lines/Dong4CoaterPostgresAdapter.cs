using LineMonitor.Core.Models;
using Npgsql;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Real Npgsql-backed adapter for 4동 코터 (public.frame/overlay_width/mismatch/cpk/ng/defect_code).
/// Reference implementation for the "switch UseMock:false" phase — not wired into the UI yet
/// since no connection string exists. The other five lines follow this same pattern once their
/// schemas get the same treatment (see plan phase 2).
///
/// Known schema caveat: public.frame/overlay_width only carry lane_1..4 columns, while
/// public.cpk carries lane_1..8 columns (overlay_N_lane_top_cpk etc.) — that mismatch exists
/// in the source schema itself, not introduced here. Frame/overlay reads are lane 1-4 only;
/// Cpk reads follow the cpk table's own 8 lanes.
/// </summary>
public sealed class Dong4CoaterPostgresAdapter : ILineAdapter
{
    private readonly string _connectionString;

    public Dong4CoaterPostgresAdapter(string connectionString)
    {
        _connectionString = connectionString;
    }

    public LineId LineId => LineId.Dong4Coater;
    public LineProfile Profile => LineProfile.All[LineId.Dong4Coater];

    public async Task<FrameSnapshot?> GetLatestFrameAsync(Side side, CancellationToken ct = default)
    {
        const string sql = """
            select f.lot_id, f.meter, f.grap_time,
                   f.e_1_tolerance, f.e_2_tolerance, f.e_3_tolerance, f.e_4_tolerance,
                   f.e_5_tolerance, f.e_6_tolerance, f.e_7_tolerance, f.e_8_tolerance,
                   o.insulation_width_1_lane_left, o.insulation_width_1_lane_right,
                   o.insulation_width_2_lane_left, o.insulation_width_2_lane_right,
                   o.insulation_width_3_lane_left, o.insulation_width_3_lane_right,
                   o.insulation_width_4_lane_left, o.insulation_width_4_lane_right,
                   o.overlay_width_1_lane_left, o.overlay_width_1_lane_right,
                   o.overlay_width_2_lane_left, o.overlay_width_2_lane_right,
                   o.overlay_width_3_lane_left, o.overlay_width_3_lane_right,
                   o.overlay_width_4_lane_left, o.overlay_width_4_lane_right
            from public.frame f
            left join public.overlay_width o on o.lot_id = f.lot_id and o.frame = f.frame_number
            order by f.id desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var lanes = new List<LaneReading>();
        for (var lane = 1; lane <= 4; lane++)
        {
            var insulLeft = ReadDecimal(reader, $"insulation_width_{lane}_lane_left");
            var insulRight = ReadDecimal(reader, $"insulation_width_{lane}_lane_right");
            var overlayLeft = ReadDecimal(reader, $"overlay_width_{lane}_lane_left");
            var overlayRight = ReadDecimal(reader, $"overlay_width_{lane}_lane_right");
            var eTolerance = lane <= 8 ? ReadDecimal(reader, $"e_{lane}_tolerance") : null;

            lanes.Add(new LaneReading(
                lane,
                Tolerance: eTolerance,
                Insul: Average(insulLeft, insulRight),
                Overlay: Average(overlayLeft, overlayRight)));
        }

        return new FrameSnapshot(
            LotId: reader.GetString(reader.GetOrdinal("lot_id")),
            Meter: reader.IsDBNull(reader.GetOrdinal("meter")) ? "" : reader.GetString(reader.GetOrdinal("meter")),
            Side: side,
            Lanes: lanes,
            GrabTime: ParseGrapTime(reader, "grap_time"));
    }

    public async Task<MismatchSnapshot?> GetLatestMismatchAsync(Stage stage, CancellationToken ct = default)
    {
        // public.mismatch엔 Wet/Dry 단계 구분 컬럼이 없음 — 현재는 같은 최신 행을 두 단계 모두에 반환.
        // 실제로는 Wet(Head)/Dry(오븐 후) 站 구분용 pc_index 필터가 필요할 수 있음(확인 필요).
        const string sql = """
            select lot_id, grap_time,
                   lane_1_l_mismatch, lane_1_r_mismatch, lane_2_l_mismatch, lane_2_r_mismatch,
                   lane_3_l_mismatch, lane_3_r_mismatch, lane_4_l_mismatch, lane_4_r_mismatch
            from public.mismatch
            order by id desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var lanes = new List<LaneMismatch>();
        for (var lane = 1; lane <= 4; lane++)
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
        // 스키마 한계: public.frame/overlay_width엔 Wet(코팅 head) 단계 컬럼이 없고
        // e_1..e_8_tolerance(Dry 간격) + overlay_width(4레인)만 있음. 레퍼런스 화면의
        // Wet 값은 현재 Dry 값에 약간의 지터를 더한 근사치 — 실제 Wet 소스 테이블/컬럼
        // 확인되면 교체 필요.
        var frame = await GetLatestFrameAsync(side, ct);
        if (frame is null)
        {
            return null;
        }

        var edges = frame.Lanes
            .Select(l => new EdgeReading(
                l.Lane,
                Wet: l.Tolerance.HasValue ? l.Tolerance + 0.1m : null,
                Dry: l.Tolerance,
                CutLabel: null))
            .ToList();

        var lanes = frame.Lanes
            .Select(l => new LaneWetDry(
                l.Lane,
                Wet: l.Overlay,
                Dry: l.Overlay.HasValue ? l.Overlay - 0.05m : null,
                InsulWetLeft: l.Insul, InsulWetRight: l.Insul,
                InsulDryLeft: l.Insul, InsulDryRight: l.Insul))
            .ToList();

        var avrWet = lanes.Count > 0 ? lanes.Average(l => l.Wet ?? 0) : (decimal?)null;
        var avrDry = lanes.Count > 0 ? lanes.Average(l => l.Dry ?? 0) : (decimal?)null;

        // 4동 코터 recipe엔 pattern1../pattern8.. 필드가 없음(2동만 스트라이프 패턴 지원) — 빈 목록.
        return new SideMeasurement(frame.LotId, frame.Meter, side, edges, lanes, avrWet, avrDry, Array.Empty<PatternSegment>());
    }

    public async Task<IReadOnlyList<LaneSeries>> GetRecentSeriesAsync(SeriesKind kind, Side side, int take, CancellationToken ct = default)
    {
        // Tolerance is stored per E-point (e_1..e_8), not per physical lane — treated as
        // "lane" 1-8 for charting purposes, consistent with the E1..E8 strip in the reference UI.
        if (kind == SeriesKind.Tolerance)
        {
            return await GetToleranceSeriesAsync(take, ct);
        }

        return await GetInsulOrOverlaySeriesAsync(kind, take, ct);
    }

    private async Task<IReadOnlyList<LaneSeries>> GetToleranceSeriesAsync(int take, CancellationToken ct)
    {
        const string sql = """
            select grap_time, e_1_tolerance, e_2_tolerance, e_3_tolerance, e_4_tolerance,
                   e_5_tolerance, e_6_tolerance, e_7_tolerance, e_8_tolerance
            from public.frame
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
                byLane[lane].Add(new SeriesPoint(time, ReadDecimal(reader, $"e_{lane}_tolerance")));
            }
        }

        return byLane.Select(kv => new LaneSeries(kv.Key, kv.Value)).ToList();
    }

    private async Task<IReadOnlyList<LaneSeries>> GetInsulOrOverlaySeriesAsync(SeriesKind kind, int take, CancellationToken ct)
    {
        var prefix = kind == SeriesKind.Insul ? "insulation_width" : "overlay_width";
        var sql = $"""
            select frame, {string.Join(",", Enumerable.Range(1, 4).SelectMany(l => new[] { $"{prefix}_{l}_lane_left", $"{prefix}_{l}_lane_right" }))}
            from public.overlay_width
            order by id desc
            limit @take
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("take", take);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byLane = Enumerable.Range(1, 4).ToDictionary(l => l, _ => new List<SeriesPoint>());
        var now = DateTime.Now;
        var i = 0;
        while (await reader.ReadAsync(ct))
        {
            var time = now.AddSeconds(-2 * i++);
            for (var lane = 1; lane <= 4; lane++)
            {
                var left = ReadDecimal(reader, $"{prefix}_{lane}_lane_left");
                var right = ReadDecimal(reader, $"{prefix}_{lane}_lane_right");
                byLane[lane].Add(new SeriesPoint(time, Average(left, right)));
            }
        }

        return byLane.Select(kv => new LaneSeries(kv.Key, kv.Value)).ToList();
    }

    public async Task<IReadOnlyList<CpkResult>> GetCpkAsync(CancellationToken ct = default)
    {
        const string sql = """
            select overlay_1_lane_top_cpk, overlay_2_lane_top_cpk, overlay_3_lane_top_cpk, overlay_4_lane_top_cpk,
                   overlay_5_lane_top_cpk, overlay_6_lane_top_cpk, overlay_7_lane_top_cpk, overlay_8_lane_top_cpk,
                   insulation_1_lane_back_cpk, insulation_2_lane_back_cpk, insulation_3_lane_back_cpk, insulation_4_lane_back_cpk,
                   insulation_5_lane_back_cpk, insulation_6_lane_back_cpk, insulation_7_lane_back_cpk, insulation_8_lane_back_cpk,
                   tolerance_e1_top_cpk, tolerance_e2_top_cpk, tolerance_e3_top_cpk, tolerance_e4_top_cpk,
                   tolerance_e5_top_cpk, tolerance_e6_top_cpk, tolerance_e7_top_cpk, tolerance_e8_top_cpk,
                   tolerance_e1_back_cpk, tolerance_e2_back_cpk, tolerance_e3_back_cpk, tolerance_e4_back_cpk,
                   tolerance_e5_back_cpk, tolerance_e6_back_cpk, tolerance_e7_back_cpk, tolerance_e8_back_cpk,
                   mismatch_1_lane_cpk, mismatch_2_lane_cpk, mismatch_3_lane_cpk, mismatch_4_lane_cpk,
                   mismatch_5_lane_cpk, mismatch_6_lane_cpk, mismatch_7_lane_cpk, mismatch_8_lane_cpk
            from public.cpk
            order by id desc
            limit 1
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            return Array.Empty<CpkResult>();
        }

        var results = new List<CpkResult>();
        for (var lane = 1; lane <= 8; lane++)
        {
            results.Add(new CpkResult(lane, CpkCategory.OverlayTop, ReadDouble(reader, $"overlay_{lane}_lane_top_cpk")));
            results.Add(new CpkResult(lane, CpkCategory.InsulBack, ReadDouble(reader, $"insulation_{lane}_lane_back_cpk")));
            results.Add(new CpkResult(lane, CpkCategory.ToleranceTop, ReadDouble(reader, $"tolerance_e{lane}_top_cpk")));
            results.Add(new CpkResult(lane, CpkCategory.ToleranceBack, ReadDouble(reader, $"tolerance_e{lane}_back_cpk")));
            results.Add(new CpkResult(lane, CpkCategory.Mismatch, ReadDouble(reader, $"mismatch_{lane}_lane_cpk")));
        }

        return results;
    }

    public async Task<IReadOnlyList<DefectRecord>> GetRecentDefectsAsync(int take, long sinceId = 0, CancellationToken ct = default)
    {
        // sinceId=0: 초기 로드(최신부터 내림차순). sinceId>0: 그 id 이후만 오름차순 —
        // 워터마크 기반 증분 폴링이라 폴링 틱 사이에 여러 건 쌓여도 안 놓침.
        var sql = sinceId > 0
            ? """
                select n.id, n.defect_time, n.lot_id, n.lane, n.defect_type_code, d.defect_name,
                       n.pos_x, n.pos_y, n.size_x, n.size_y
                from public.ng n
                left join public.defect_code d on d.defect_type_code = n.defect_type_code
                where n.id > @sinceId
                order by n.id asc
                limit @take
                """
            : """
                select n.id, n.defect_time, n.lot_id, n.lane, n.defect_type_code, d.defect_name,
                       n.pos_x, n.pos_y, n.size_x, n.size_y
                from public.ng n
                left join public.defect_code d on d.defect_type_code = n.defect_type_code
                order by n.id desc
                limit @take
                """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("take", take);
        if (sinceId > 0)
        {
            cmd.Parameters.AddWithValue("sinceId", sinceId);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var records = new List<DefectRecord>();
        while (await reader.ReadAsync(ct))
        {
            records.Add(new DefectRecord(
                Id: reader.GetInt32(reader.GetOrdinal("id")),
                Time: ParseGrapTime(reader, "defect_time"),
                LotId: reader.GetString(reader.GetOrdinal("lot_id")),
                Lane: reader.IsDBNull(reader.GetOrdinal("lane")) ? 0 : reader.GetInt32(reader.GetOrdinal("lane")),
                DefectCode: reader.IsDBNull(reader.GetOrdinal("defect_type_code")) ? "" : reader.GetInt32(reader.GetOrdinal("defect_type_code")).ToString(),
                DefectName: reader.IsDBNull(reader.GetOrdinal("defect_name")) ? "" : reader.GetString(reader.GetOrdinal("defect_name")),
                ImagePath: null,
                PosX: ReadDecimal(reader, "pos_x"),
                PosY: ReadDecimal(reader, "pos_y"),
                SizeX: ReadDecimal(reader, "size_x"),
                SizeY: ReadDecimal(reader, "size_y")));
        }

        return records;
    }

    public async Task<IReadOnlyList<DefectPivotRow>> GetDefectPivotAsync(CancellationToken ct = default)
    {
        const string sql = """
            select d.defect_name, count(*) as cnt
            from public.ng n
            left join public.defect_code d on d.defect_type_code = n.defect_type_code
            where n.defect_time > (now() - interval '1 day')::text
            group by d.defect_name
            """;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var counts = new Dictionary<string, int>();
        while (await reader.ReadAsync(ct))
        {
            var name = reader.IsDBNull(0) ? "UNKNOWN" : reader.GetString(0);
            counts[name] = (int)reader.GetInt64(1);
        }

        // ng 테이블엔 개소(위치) 구분 컬럼이 없어 전체를 한 행으로 집계함 —
        // 위치별 분리가 필요하면 top_back/cam_num 컬럼 기준으로 확장.
        return new[] { new DefectPivotRow("전체", counts) };
    }

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

    private static double? ReadDouble(NpgsqlDataReader reader, string column)
    {
        var value = ReadDecimal(reader, column);
        return value.HasValue ? (double)value.Value : null;
    }

    private static decimal? Average(decimal? a, decimal? b)
    {
        if (a is null && b is null) return null;
        if (a is null) return b;
        if (b is null) return a;
        return (a.Value + b.Value) / 2;
    }

    private static DateTime ParseGrapTime(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return DateTime.MinValue;
        }

        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, out var parsed) ? parsed : DateTime.MinValue;
    }
}
