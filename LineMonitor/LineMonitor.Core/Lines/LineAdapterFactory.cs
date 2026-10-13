using LineMonitor.Core.Configuration;
using LineMonitor.Core.Models;

namespace LineMonitor.Core.Lines;

/// <summary>
/// Builds one ILineAdapter per line. Falls back to Mock whenever UseMock is on, or a
/// line has no real adapter / connection string yet — so adding new real adapters is
/// purely additive (register them in the switch below).
/// </summary>
public sealed class LineAdapterFactory
{
    private readonly LineMonitorOptions _options;
    private readonly Func<int, string?>? _mockSampleImageProvider;

    public LineAdapterFactory(LineMonitorOptions options, Func<int, string?>? mockSampleImageProvider = null)
    {
        _options = options;
        _mockSampleImageProvider = mockSampleImageProvider;
    }

    public ILineAdapter Create(LineId lineId, ElectrodeType electrodeType = ElectrodeType.Positive)
    {
        var profile = LineProfile.All[lineId];
        var connectionString = _options.ConnectionStrings.GetValueOrDefault($"{lineId}.{electrodeType}")
            ?? _options.ConnectionStrings.GetValueOrDefault(lineId.ToString());

        if (!_options.UseMock &&
            !string.IsNullOrWhiteSpace(connectionString))
        {
            ILineAdapter? real = lineId switch
            {
                LineId.Dong4Coater => new Dong4CoaterPostgresAdapter(connectionString),
                LineId.Dong9Coater => new Dong9CoaterPostgresAdapter(connectionString),
                // 나머지 4개 라인은 phase 2에서 같은 패턴으로 추가.
                _ => null
            };

            if (real is not null)
            {
                return real;
            }
        }

        return new MockLineAdapter(profile, _mockSampleImageProvider);
    }

    public IReadOnlyList<ILineAdapter> CreateAll() =>
        LineProfile.All.Keys.Select(lineId => Create(lineId)).ToList();
}
