using System.Globalization;

namespace InsightEngine.Domain.Recommendations.Scoring;

public static class Normalization
{
    public static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0d, 1d);
    }

    public static double RobustVarianceNormalize(double? value, IReadOnlyCollection<double> allValues)
    {
        if (value is null)
        {
            return 0;
        }

        var list = allValues
            .Where(item => !double.IsNaN(item) && !double.IsInfinity(item))
            .OrderBy(item => item)
            .ToList();

        if (list.Count == 0)
        {
            return 0;
        }

        if (list.Count < 4)
        {
            var min = list.Min();
            var max = list.Max();
            if (max <= min)
            {
                return 0;
            }

            return Clamp01((value.Value - min) / (max - min));
        }

        var p50 = Percentile(list, 0.50);
        var p90 = Percentile(list, 0.90);
        if (p90 <= p50)
        {
            return 0;
        }

        return Clamp01((value.Value - p50) / (p90 - p50));
    }

    public static double CardinalityPenalty(long distinctCount, long rowCount)
    {
        if (rowCount <= 0)
        {
            return 0;
        }

        if (distinctCount <= 3)
        {
            return 0;
        }

        var ratio = distinctCount / (double)rowCount;
        if (ratio <= 0.4)
        {
            return 0;
        }

        return Clamp01((ratio - 0.4) / 0.6);
    }

    public static string BuildMagnitudeBand(double? value)
    {
        if (value is null)
        {
            return "unknown";
        }

        var abs = Math.Abs(value.Value);
        if (abs < 1e-3) return "lt_1e-3";
        if (abs < 1e0) return "1e-3_1e0";
        if (abs < 1e3) return "1e0_1e3";
        if (abs < 1e6) return "1e3_1e6";
        return "gte_1e6";
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var p = Math.Clamp(percentile, 0d, 1d);
        var rank = p * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return sorted[low];
        }

        var weight = rank - low;
        return sorted[low] + ((sorted[high] - sorted[low]) * weight);
    }
}
