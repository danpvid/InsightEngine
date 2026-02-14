namespace InsightEngine.Domain.Interfaces;

/// <summary>
/// Service for caching metadata (profile and recommendations) to filesystem
/// </summary>
public interface IMetadataCacheService
{
    /// <summary>
    /// Get cached profile for a dataset
    /// </summary>
    Task<T?> GetCachedProfileAsync<T>(Guid datasetId) where T : class;

    /// <summary>
    /// Cache profile for a dataset
    /// </summary>
    Task SetCachedProfileAsync<T>(Guid datasetId, T profile) where T : class;

    /// <summary>
    /// Get cached recommendations for a dataset
    /// </summary>
    Task<T?> GetCachedRecommendationsAsync<T>(Guid datasetId) where T : class;

    /// <summary>
    /// Cache recommendations for a dataset
    /// </summary>
    Task SetCachedRecommendationsAsync<T>(Guid datasetId, T recommendations) where T : class;

    /// <summary>
    /// Clear all cached metadata for a dataset
    /// </summary>
    Task ClearCacheAsync(Guid datasetId);

    /// <summary>
    /// Check if profile cache exists for a dataset
    /// </summary>
    Task<bool> HasCachedProfileAsync(Guid datasetId);

    /// <summary>
    /// Check if recommendations cache exists for a dataset
    /// </summary>
    Task<bool> HasCachedRecommendationsAsync(Guid datasetId);
}
