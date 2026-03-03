using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace InsightEngine.Infra.Data.Repositories;

public class DashboardCacheRepository : Repository<DashboardCacheEntry>, IDashboardCacheRepository
{
    public DashboardCacheRepository(InsightEngineContext context) : base(context)
    {
    }

    public async Task<DashboardCacheEntry?> GetAsync(
        Guid ownerUserId,
        Guid datasetId,
        string version,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(
            entry =>
                entry.OwnerUserId == ownerUserId
                && entry.DatasetId == datasetId
                && entry.Version == version,
            cancellationToken);
    }

    public async Task RemoveByDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        var entries = await _dbSet
            .Where(entry => entry.DatasetId == datasetId)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return;
        }

        _dbSet.RemoveRange(entries);
    }
}
