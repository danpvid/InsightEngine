using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class ChartViewMeta
{
    public ChartViewKind Kind { get; set; } = ChartViewKind.Base;
    public PercentileKind? PercentileKind { get; set; }
    public PercentileMode? PercentileMode { get; set; }
}
