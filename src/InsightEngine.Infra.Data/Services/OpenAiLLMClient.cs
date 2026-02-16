using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;

namespace InsightEngine.Infra.Data.Services;

public class OpenAiLLMClient : ILLMClient
{
    private const string Message =
        "OpenAI provider is configured as a placeholder only. Use Provider=None or Provider=LocalHttp in this environment.";

    public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Message);
    }

    public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Message);
    }
}
