using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class LLMRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public Dictionary<string, object?> ContextObjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public LLMResponseFormat ResponseFormat { get; set; } = LLMResponseFormat.Text;
}
