using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface ILLMClient
{
    Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default);
    Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default);
}
