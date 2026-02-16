using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class NullLLMClient : ILLMClient
{
    private readonly ILogger<NullLLMClient> _logger;

    public NullLLMClient(ILogger<NullLLMClient> logger)
    {
        _logger = logger;
    }

    public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LLM request skipped because provider is disabled.");
        return Task.FromResult(Result.Failure<LLMResponse>("LLM provider is disabled."));
    }

    public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LLM JSON request skipped because provider is disabled.");
        return Task.FromResult(Result.Failure<LLMResponse>("LLM provider is disabled."));
    }
}
