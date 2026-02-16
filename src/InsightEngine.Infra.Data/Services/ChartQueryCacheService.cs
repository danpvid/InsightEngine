using System;
using System.Collections.Concurrent;
using System.Linq;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class ChartQueryCacheService : IChartQueryCache
{
    private readonly IMemoryCache _cache;
    private readonly ChartCacheSettings _settings;
    private readonly ConcurrentDictionary<string, byte> _keys = new();

    public ChartQueryCacheService(
        IMemoryCache cache,
        IOptions<ChartCacheSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value;
    }

    public Task<ChartExecutionResponse?> GetAsync(Guid datasetId, string recommendationId, string queryHash)
    {
        var key = BuildKey(datasetId, recommendationId, queryHash);
        return Task.FromResult(_cache.TryGetValue(key, out ChartExecutionResponse? cached) ? cached : null);
    }

    public Task SetAsync(Guid datasetId, string recommendationId, string queryHash, ChartExecutionResponse response)
    {
        var key = BuildKey(datasetId, recommendationId, queryHash);
        var ttl = _settings.TtlSeconds <= 0 ? 300 : _settings.TtlSeconds;

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttl)
        };

        _cache.Set(key, response, options);
        _keys.TryAdd(key, 0);

        return Task.CompletedTask;
    }

    public Task InvalidateDatasetAsync(Guid datasetId)
    {
        var prefix = $"{datasetId}:";
        foreach (var key in _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    private static string BuildKey(Guid datasetId, string recommendationId, string queryHash)
    {
        return $"{datasetId}:{recommendationId}:{queryHash}";
    }
}
