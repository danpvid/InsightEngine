using InsightEngine.Domain.Entities;

namespace InsightEngine.Domain.Interfaces;

public interface IDashboardCacheRepository : IRepository<DashboardCacheEntry>
{
    Task<DashboardCacheEntry?> GetAsync(
        Guid ownerUserId,
        Guid datasetId,
        string version,
        CancellationToken cancellationToken = default);

    Task RemoveByDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
