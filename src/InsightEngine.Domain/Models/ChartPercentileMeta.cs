using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class ChartPercentileMeta
{
    public bool Supported { get; set; }
    public PercentileMode Mode { get; set; } = PercentileMode.NotApplicable;
    public List<PercentileKind> Available { get; set; } = new();
    public string? Reason { get; set; }
    public List<PercentileValue> Values { get; set; } = new();
}

public class PercentileValue
{
    public PercentileKind Kind { get; set; }
    public double Value { get; set; }
}
