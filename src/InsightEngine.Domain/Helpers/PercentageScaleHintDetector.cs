using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Helpers;

public static class PercentageScaleHintDetector
{
    public static PercentageScaleHint Detect(double? min, double? max, double? mean)
    {
        if (!max.HasValue || !mean.HasValue)
        {
            return PercentageScaleHint.Unknown;
        }

        if (max.Value <= 1.2 && mean.Value <= 1.0)
        {
            return PercentageScaleHint.ZeroToOne;
        }

        if (max.Value <= 120 && (!min.HasValue || min.Value >= -20))
        {
            return PercentageScaleHint.ZeroToHundred;
        }

        return PercentageScaleHint.Unknown;
    }
}
