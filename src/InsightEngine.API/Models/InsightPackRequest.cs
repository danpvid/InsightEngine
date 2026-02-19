using InsightEngine.Domain.Models;

namespace InsightEngine.API.Models;

public class InsightPackRequest
{
    public string RecommendationId { get; set; } = string.Empty;
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<string> Filters { get; set; } = new();
    public ScenarioRequest? Scenario { get; set; }
    public int? Horizon { get; set; }
    public bool SensitiveMode { get; set; }
}

public class InsightPackAskApiRequest : InsightPackRequest
{
    public string Question { get; set; } = string.Empty;
}
