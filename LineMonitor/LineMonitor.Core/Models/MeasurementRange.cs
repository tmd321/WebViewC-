using System.Diagnostics;

namespace LineMonitor.Core.Models;

public sealed record MeasurementRange(decimal? Min, decimal? Max)
{
    public bool IsOutOfRange(decimal? value) =>
        value is { } actual && Min is { } min && Max is { } max && min <= max
        && (actual < min || actual > max);

#if DEBUG
    static MeasurementRange()
    {
        var range = new MeasurementRange(-1m, 1m);
        Debug.Assert(!range.IsOutOfRange(-1m) && !range.IsOutOfRange(1m) && !range.IsOutOfRange(0m));
        Debug.Assert(range.IsOutOfRange(-1.01m) && range.IsOutOfRange(1.01m));
        Debug.Assert(!range.IsOutOfRange(null) && !new MeasurementRange(null, 1m).IsOutOfRange(2m));
        Debug.Assert(!new MeasurementRange(2m, 1m).IsOutOfRange(0m));
    }
#endif
}
