using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models.Insights;

namespace InsightEngine.Domain.Models;

public class SemanticInsightPack
{
    public string Version { get; set; } = "2.0";
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string EvidenceVersion { get; set; } = string.Empty;
    public string PercentageScaleHint { get; set; } = "unknown";
    public List<InsightPackDriver> TargetDrivers { get; set; } = new();
    public List<InsightPackCorrelation> Correlations { get; set; } = new();
    public List<EvidenceFact> Facts { get; set; } = new();
    public InsightPackV2? PackV2 { get; set; }
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
    public InsightAskStructuredResponse? AnswerJson { get; set; }
    public List<InsightResolvedEvidence> EvidenceResolved { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
    public SemanticInsightPack Pack { get; set; } = new();
}

public class InsightAskStructuredResponse
{
    public List<string> ExecutiveSummary { get; set; } = new();
    public List<InsightAskFinding> KeyFindings { get; set; } = new();
    public InsightAskDriverGroups TopDrivers { get; set; } = new();
    public List<InsightAskOffender> Offenders { get; set; } = new();
    public List<InsightAskRecommendation> Recommendations { get; set; } = new();
    public List<string> Caveats { get; set; } = new();
    public List<string> FollowUpQuestions { get; set; } = new();
}

public class InsightAskFinding
{
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public string Confidence { get; set; } = "Medium";
}

public class InsightAskDriverGroups
{
    public List<InsightAskDriver> Negative { get; set; } = new();
    public List<InsightAskDriver> Positive { get; set; } = new();
}

public class InsightAskDriver
{
    public string Name { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public string Confidence { get; set; } = "Medium";
}

public class InsightAskOffender
{
    public string Name { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public string Confidence { get; set; } = "Medium";
}

public class InsightAskRecommendation
{
    public string Action { get; set; } = string.Empty;
    public string ExpectedImpact { get; set; } = "IncreaseTarget";
    public string Why { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public string Risk { get; set; } = string.Empty;
}

public class InsightResolvedEvidence
{
    public string EvidenceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
