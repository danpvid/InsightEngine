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
}

public class AiInsightSummaryResult
{
    public AiInsightSummary InsightSummary { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
}
