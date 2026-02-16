using InsightEngine.Domain.Models;

namespace InsightEngine.API.Models;

public class DeepInsightsApiRequest
{
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<string> Filters { get; set; } = new();
    public ScenarioRequest? Scenario { get; set; }
    public int? Horizon { get; set; }
    public bool SensitiveMode { get; set; }
    public bool IncludeEvidence { get; set; } = true;
}
