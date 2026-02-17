using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.ExternalService.Services;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class LLMClientRouter : ILLMClient
{
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly LocalHttpLLMClient _localHttpClient;
    private readonly NullLLMClient _nullClient;
    private readonly OpenAiLLMClient _openAiClient;

    public LLMClientRouter(
        IOptionsMonitor<LLMSettings> settingsMonitor,
        LocalHttpLLMClient localHttpClient,
        NullLLMClient nullClient,
        OpenAiLLMClient openAiClient)
    {
        _settingsMonitor = settingsMonitor;
        _localHttpClient = localHttpClient;
        _nullClient = nullClient;
        _openAiClient = openAiClient;
    }

    public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return ResolveClient().GenerateTextAsync(request, cancellationToken);
    }

    public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return ResolveClient().GenerateJsonAsync(request, cancellationToken);
    }

    private ILLMClient ResolveClient()
    {
        return _settingsMonitor.CurrentValue.Provider switch
        {
            LLMProvider.LocalHttp => _localHttpClient,
            LLMProvider.OpenAI => _openAiClient,
            _ => _nullClient
        };
    }
}
