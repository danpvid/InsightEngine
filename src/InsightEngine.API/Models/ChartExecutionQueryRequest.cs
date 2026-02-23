namespace InsightEngine.API.Models;

public class ChartExecutionQueryRequest
{
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? XColumn { get; set; }
    public string? YColumn { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public string[] Filters { get; set; } = [];
    public string? View { get; set; }
    public string? Percentile { get; set; }
    public string? Mode { get; set; }
    public string? PercentileTarget { get; set; }
}
