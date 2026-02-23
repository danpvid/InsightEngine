using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Queries.DataSet;

public class BuildInsightPackQuery : Query<SemanticInsightPackResult>, IAiQueryBase
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string Language { get; set; } = "pt-br";
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
    public ScenarioRequest? Scenario { get; set; }
    public int? Horizon { get; set; }
    public bool SensitiveMode { get; set; }
    public string RequesterKey { get; set; } = "anonymous";
}
