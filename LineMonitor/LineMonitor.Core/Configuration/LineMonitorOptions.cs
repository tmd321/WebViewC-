namespace LineMonitor.Core.Configuration;

/// <summary>
/// Bound from appsettings.json. UseMock=true lets the app run with no DB at all —
/// flip to false and fill ConnectionStrings once a line's Postgres credentials arrive.
/// </summary>
public sealed class LineMonitorOptions
{
    public bool UseMock { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 2;
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
}
