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
                LineId.Dong4Coater => new Dong4CoaterPostgresAdapter(connectionString, electrodeType == ElectrodeType.Positive),
                LineId.Dong9Coater => new Dong9CoaterPostgresAdapter(connectionString, electrodeType == ElectrodeType.Positive),
                // 나머지 라인은 실제 스키마에 맞는 어댑터가 구현될 때 추가한다.
                _ => null
            };

            if (real is not null)
            {
                return real;
            }
        }

        return new MockLineAdapter(profile, _mockSampleImageProvider);
    }
}
