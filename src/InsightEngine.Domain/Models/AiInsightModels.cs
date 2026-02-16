using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class AiInsightSummary
{
    public string Headline { get; set; } = string.Empty;
    public List<string> BulletPoints { get; set; } = new();
    public List<string> Cautions { get; set; } = new();
    public List<string> NextQuestions { get; set; } = new();
    public double Confidence { get; set; }
}

public class AiGenerationMeta
{
    public LLMProvider Provider { get; set; } = LLMProvider.None;
    public string Model { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public bool CacheHit { get; set; }
    public bool FallbackUsed { get; set; }
    public string? FallbackReason { get; set; }
    public int? EvidenceBytes { get; set; }
    public int? OutputBytes { get; set; }
    public string? ValidationStatus { get; set; }
}

public class AiInsightSummaryResult
{
    public AiInsightSummary InsightSummary { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
}

public class ChartExplanation
{
    public List<string> Explanation { get; set; } = new();
    public List<string> KeyTakeaways { get; set; } = new();
    public List<string> PotentialCauses { get; set; } = new();
    public List<string> Caveats { get; set; } = new();
    public List<string> SuggestedNextSteps { get; set; } = new();
    public List<string> QuestionsToAsk { get; set; } = new();
}

public class ChartExplanationResult
{
    public ChartExplanation Explanation { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
}

public class AskAnalysisPlanRequest
{
    public Guid DatasetId { get; set; }
    public string Language { get; set; } = "pt-br";
    public string Question { get; set; } = string.Empty;
    public Dictionary<string, object?> CurrentView { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AskAnalysisPlan
{
    public string Intent { get; set; } = "Compare";
    public string SuggestedChartType { get; set; } = "Bar";
    public AskProposedDimensions ProposedDimensions { get; set; } = new();
    public List<AskSuggestedFilter> SuggestedFilters { get; set; } = new();
    public List<string> Reasoning { get; set; } = new();
}

public class AskProposedDimensions
{
    public string? X { get; set; }
    public string? Y { get; set; }
    public string? GroupBy { get; set; }
}

public class AskSuggestedFilter
{
    public string Column { get; set; } = string.Empty;
    public string Operator { get; set; } = "Eq";
    public List<string> Values { get; set; } = new();
}

public class AskAnalysisPlanResult
{
    public AskAnalysisPlan Plan { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
}
