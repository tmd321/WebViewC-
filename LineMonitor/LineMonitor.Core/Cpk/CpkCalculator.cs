namespace LineMonitor.Core.Cpk;

/// <summary>
/// Standard Cpk = min((USL-mean)/3sigma, (mean-LSL)/3sigma).
/// Used by line adapters that don't have a native cpk table (only 4동 코터 does) —
/// they feed recent measured values plus the spec limits pulled from
/// recipe/recipe_overlay/recipe_insul/model_width_spec.
/// </summary>
public static class CpkCalculator
{
    public static double? Calculate(IReadOnlyList<decimal> values, decimal? lsl, decimal? usl)
    {
        if (values.Count < 2 || lsl is null || usl is null)
        {
            return null;
        }

        var mean = (double)values.Average();
        var variance = values.Sum(v => Math.Pow((double)v - mean, 2)) / (values.Count - 1);
        var sigma = Math.Sqrt(variance);

        if (sigma <= 0)
        {
            return null;
        }

        var upper = ((double)usl - mean) / (3 * sigma);
        var lower = (mean - (double)lsl) / (3 * sigma);
        return Math.Min(upper, lower);
    }
}
