using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class LLMResponse
{
    public string? Text { get; set; }
    public string? Json { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public LLMProvider Provider { get; set; } = LLMProvider.None;
    public LLMTokenUsage? TokenUsage { get; set; }
    public long DurationMs { get; set; }
    public bool CacheHit { get; set; }
}
