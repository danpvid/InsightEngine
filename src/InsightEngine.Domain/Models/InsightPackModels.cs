using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class SemanticInsightPack
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string EvidenceVersion { get; set; } = string.Empty;
    public string PercentageScaleHint { get; set; } = "unknown";
    public List<InsightPackDriver> TargetDrivers { get; set; } = new();
    public List<InsightPackCorrelation> Correlations { get; set; } = new();
    public List<EvidenceFact> Facts { get; set; } = new();
}

public class InsightPackDriver
{
    public string Name { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class InsightPackCorrelation
{
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
    public double Correlation { get; set; }
    public string Direction { get; set; } = "neutral";
}

public class SemanticInsightPackResult
{
    public SemanticInsightPack Pack { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
}

public class InsightPackAskRequest
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Language { get; set; } = "pt-br";
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
    public bool SensitiveMode { get; set; }
}

public class InsightPackAskResult
{
    public string Answer { get; set; } = string.Empty;
    public List<string> Caveats { get; set; } = new();
    public List<DeepInsightCitation> Citations { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
    public SemanticInsightPack Pack { get; set; } = new();
}
