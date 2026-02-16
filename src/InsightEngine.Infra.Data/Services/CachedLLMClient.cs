using InsightEngine.Domain.Core;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class CachedLLMClient : ILLMClient
{
    private readonly LLMClientRouter _innerClient;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<LLMSettings> _llmSettingsMonitor;
    private readonly IOptionsMonitor<InsightEngineSettings> _runtimeSettingsMonitor;

    public CachedLLMClient(
        LLMClientRouter innerClient,
        IMemoryCache cache,
        IOptionsMonitor<LLMSettings> llmSettingsMonitor,
        IOptionsMonitor<InsightEngineSettings> runtimeSettingsMonitor)
    {
        _innerClient = innerClient;
        _cache = cache;
        _llmSettingsMonitor = llmSettingsMonitor;
        _runtimeSettingsMonitor = runtimeSettingsMonitor;
    }

    public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(request, isJsonRequest: false, cancellationToken);
    }

    public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(request, isJsonRequest: true, cancellationToken);
    }

    private async Task<Result<LLMResponse>> ExecuteAsync(
        LLMRequest request,
        bool isJsonRequest,
        CancellationToken cancellationToken)
    {
        var llmSettings = _llmSettingsMonitor.CurrentValue;
        var model = llmSettings.LocalHttp.Model;
        var cacheKey = LLMCacheKeyHelper.Build(request, llmSettings.Provider, model);

        if (llmSettings.EnableCaching && _cache.TryGetValue<LLMResponse>(cacheKey, out var cachedResponse))
        {
            return Result.Success(CloneWithCacheHit(cachedResponse!, cacheHit: true));
        }

        var result = isJsonRequest
            ? await _innerClient.GenerateJsonAsync(request, cancellationToken)
            : await _innerClient.GenerateTextAsync(request, cancellationToken);

        if (!result.IsSuccess || result.Data == null)
        {
            return result;
        }

        var response = CloneWithCacheHit(result.Data, cacheHit: false);
        if (llmSettings.EnableCaching)
        {
            var ttlSeconds = Math.Max(1, _runtimeSettingsMonitor.CurrentValue.CacheTtlSeconds);
            _cache.Set(cacheKey, response, TimeSpan.FromSeconds(ttlSeconds));
        }

        return Result.Success(response);
    }

    private static LLMResponse CloneWithCacheHit(LLMResponse source, bool cacheHit)
    {
        return new LLMResponse
        {
            Text = source.Text,
            Json = source.Json,
            ModelId = source.ModelId,
            Provider = source.Provider,
            DurationMs = source.DurationMs,
            CacheHit = cacheHit,
            TokenUsage = source.TokenUsage == null
                ? null
                : new LLMTokenUsage
                {
                    PromptTokens = source.TokenUsage.PromptTokens,
                    CompletionTokens = source.TokenUsage.CompletionTokens,
                    TotalTokens = source.TokenUsage.TotalTokens
                }
        };
    }
}
