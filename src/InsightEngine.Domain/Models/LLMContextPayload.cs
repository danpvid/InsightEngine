namespace InsightEngine.Domain.Models;

public class LLMChartContextRequest
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
    public Dictionary<string, object?> ScenarioMeta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LLMAskContextRequest
{
    public Guid DatasetId { get; set; }
    public Dictionary<string, object?> CurrentView { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class LLMContextPayload
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public InsightSummary? HeuristicSummary { get; set; }
    public ChartExecutionMeta? ChartMeta { get; set; }
    public Dictionary<string, object?> ContextObjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int SerializedBytes { get; set; }
    public bool Truncated { get; set; }
}
