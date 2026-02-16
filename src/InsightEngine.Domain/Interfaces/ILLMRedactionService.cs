namespace InsightEngine.Domain.Interfaces;

public interface ILLMRedactionService
{
    Dictionary<string, object?> RedactContext(Dictionary<string, object?> context);
}
