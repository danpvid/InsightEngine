using System.Text.Json;
using System.Text.Json.Serialization;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class IndexStore : IIndexStore
{
    private readonly IFileStorageService _fileStorageService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<IndexStore> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly JsonSerializerOptions _jsonOptions;

    public IndexStore(
        IFileStorageService fileStorageService,
        IMemoryCache memoryCache,
        IOptions<InsightEngineSettings> runtimeSettings,
        ILogger<IndexStore> logger)
    {
        _fileStorageService = fileStorageService;
        _memoryCache = memoryCache;
        _logger = logger;

        var ttlSeconds = Math.Clamp(runtimeSettings.Value.CacheTtlSeconds, 30, 3600);
        _cacheTtl = TimeSpan.FromSeconds(ttlSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task SaveAsync(DatasetIndex index, CancellationToken cancellationToken = default)
    {
        var path = GetIndexPath(index.DatasetId);
        EnsureDirectory(index.DatasetId);

        var json = JsonSerializer.Serialize(index, _jsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);

        _memoryCache.Set(GetIndexCacheKey(index.DatasetId), index, _cacheTtl);
        _logger.LogInformation("Persisted dataset index for {DatasetId} to {Path}", index.DatasetId, path);
    }

    public async Task<DatasetIndex?> LoadAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(GetIndexCacheKey(datasetId), out DatasetIndex? cached) && cached != null)
        {
            return cached;
        }

        var path = GetIndexPath(datasetId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var index = JsonSerializer.Deserialize<DatasetIndex>(json, _jsonOptions);
            if (index != null)
            {
                _memoryCache.Set(GetIndexCacheKey(datasetId), index, _cacheTtl);
            }

            return index;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dataset index from {Path}", path);
            return null;
        }
    }

    public async Task SaveStatusAsync(DatasetIndexStatus status, CancellationToken cancellationToken = default)
    {
        var path = GetStatusPath(status.DatasetId);
        EnsureDirectory(status.DatasetId);

        status.UpdatedAtUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(status, _jsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);

        _memoryCache.Set(GetStatusCacheKey(status.DatasetId), status, _cacheTtl);
    }

    public async Task<DatasetIndexStatus> LoadStatusAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(GetStatusCacheKey(datasetId), out DatasetIndexStatus? cached) && cached != null)
        {
            return cached;
        }

        var path = GetStatusPath(datasetId);
        if (!File.Exists(path))
        {
            return new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = Domain.Enums.IndexBuildState.NotBuilt,
                Message = "Index not built yet."
            };
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var status = JsonSerializer.Deserialize<DatasetIndexStatus>(json, _jsonOptions) ?? new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = Domain.Enums.IndexBuildState.NotBuilt,
                Message = "Index status unavailable."
            };

            _memoryCache.Set(GetStatusCacheKey(datasetId), status, _cacheTtl);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load index status from {Path}", path);
            return new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = Domain.Enums.IndexBuildState.Failed,
                Message = "Failed to read index status file."
            };
        }
    }

    public Task InvalidateAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        _memoryCache.Remove(GetIndexCacheKey(datasetId));
        _memoryCache.Remove(GetStatusCacheKey(datasetId));

        DeleteIfExists(GetIndexPath(datasetId));
        DeleteIfExists(GetStatusPath(datasetId));

        return Task.CompletedTask;
    }

    private void EnsureDirectory(Guid datasetId)
    {
        var path = GetDatasetDirectory(datasetId);
        Directory.CreateDirectory(path);
    }

    private string GetDatasetDirectory(Guid datasetId)
    {
        return Path.Combine(_fileStorageService.GetStoragePath(), datasetId.ToString("D"));
    }

    private string GetIndexPath(Guid datasetId)
    {
        return Path.Combine(GetDatasetDirectory(datasetId), "index.json");
    }

    private string GetStatusPath(Guid datasetId)
    {
        return Path.Combine(GetDatasetDirectory(datasetId), "index.status.json");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetIndexCacheKey(Guid datasetId) => $"dataset-index:{datasetId:D}";
    private static string GetStatusCacheKey(Guid datasetId) => $"dataset-index-status:{datasetId:D}";
}
