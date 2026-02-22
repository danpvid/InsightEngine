namespace InsightEngine.API.Models;

public class InsightPackQueryRequest
{
    public string RecommendationId { get; set; } = string.Empty;
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public string[] Filters { get; set; } = [];
    public string? Month { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SegmentColumn { get; set; }
    public string? SegmentValue { get; set; }
    public string OutputMode { get; set; } = "DeepDive";
    public int? Horizon { get; set; }
    public bool SensitiveMode { get; set; }
}
