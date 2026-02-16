namespace InsightEngine.API.Models;

public class AiChartRequest
{
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<string> Filters { get; set; } = new();
    public Dictionary<string, object?> ScenarioMeta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
