using InsightEngine.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InsightEngine.Infra.Data.Services;

/// <summary>
/// Implementation of metadata cache service using filesystem storage
/// </summary>
public class MetadataCacheService : IMetadataCacheService
{
    private readonly ILogger<MetadataCacheService> _logger;
    private readonly string _cacheDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public MetadataCacheService(
        IConfiguration configuration,
        ILogger<MetadataCacheService> logger)
    {
        _logger = logger;
        
        // Get cache directory from configuration or use default
        var uploadPath = configuration["UploadSettings:UploadPath"] ?? "uploads";
        _cacheDirectory = Path.Combine(uploadPath, ".cache");
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);

        // Configure JSON options for cache serialization
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true, // Pretty print for debugging
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<T?> GetCachedProfileAsync<T>(Guid datasetId) where T : class
    {
        var filePath = GetProfileCachePath(datasetId);
        return await ReadCacheFileAsync<T>(filePath, "profile");
    }

    public async Task SetCachedProfileAsync<T>(Guid datasetId, T profile) where T : class
    {
        var filePath = GetProfileCachePath(datasetId);
        await WriteCacheFileAsync(filePath, profile, "profile");
    }

    public async Task<T?> GetCachedRecommendationsAsync<T>(Guid datasetId) where T : class
    {
        var filePath = GetRecommendationsCachePath(datasetId);
        return await ReadCacheFileAsync<T>(filePath, "recommendations");
    }

    public async Task SetCachedRecommendationsAsync<T>(Guid datasetId, T recommendations) where T : class
    {
        var filePath = GetRecommendationsCachePath(datasetId);
        await WriteCacheFileAsync(filePath, recommendations, "recommendations");
    }

    public async Task ClearCacheAsync(Guid datasetId)
    {
        var profilePath = GetProfileCachePath(datasetId);
        var recommendationsPath = GetRecommendationsCachePath(datasetId);

        await Task.Run(() =>
        {
            if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
                _logger.LogInformation("Cleared profile cache for dataset {DatasetId}", datasetId);
            }

            if (File.Exists(recommendationsPath))
            {
                File.Delete(recommendationsPath);
                _logger.LogInformation("Cleared recommendations cache for dataset {DatasetId}", datasetId);
            }
        });
    }

    public Task<bool> HasCachedProfileAsync(Guid datasetId)
    {
        var filePath = GetProfileCachePath(datasetId);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<bool> HasCachedRecommendationsAsync(Guid datasetId)
    {
        var filePath = GetRecommendationsCachePath(datasetId);
        return Task.FromResult(File.Exists(filePath));
    }

    // Private helper methods

    private string GetProfileCachePath(Guid datasetId)
    {
        return Path.Combine(_cacheDirectory, $"{datasetId}_profile.json");
    }

    private string GetRecommendationsCachePath(Guid datasetId)
    {
        return Path.Combine(_cacheDirectory, $"{datasetId}_recommendations.json");
    }

    private async Task<T?> ReadCacheFileAsync<T>(string filePath, string cacheType) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("Cache miss for {CacheType}: {FilePath}", cacheType, filePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            
            _logger.LogInformation("Cache hit for {CacheType}: {FilePath}", cacheType, filePath);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cache file for {CacheType}: {FilePath}", cacheType, filePath);
            return null;
        }
    }

    private async Task WriteCacheFileAsync<T>(string filePath, T data, string cacheType) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("Cached {CacheType} to {FilePath}", cacheType, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write cache file for {CacheType}: {FilePath}", cacheType, filePath);
            // Don't throw - cache failure shouldn't break the application
        }
    }
}
