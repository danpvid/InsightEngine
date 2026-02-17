namespace InsightEngine.Domain.Models;

public class ChartPercentileComputationResult
{
    public ChartPercentileMeta Percentiles { get; set; } = new();
    public ChartViewMeta View { get; set; } = new();
    public EChartsOption? Option { get; set; }
}
