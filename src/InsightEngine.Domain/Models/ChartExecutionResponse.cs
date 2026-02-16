namespace InsightEngine.Domain.Models;

/// <summary>
/// Resposta completa de execução de chart com dados e telemetria (Domain layer)
/// </summary>
public class ChartExecutionResponse
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public ChartExecutionResult ExecutionResult { get; set; } = null!;
    public InsightSummary? InsightSummary { get; set; }
    public long TotalExecutionMs { get; set; }
    public string QueryHash { get; set; } = string.Empty;
    public bool CacheHit { get; set; }
}
