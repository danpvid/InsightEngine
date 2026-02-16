using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace InsightEngine.Infra.Data.Repositories;

public class DataSetRepository : Repository<DataSet>, IDataSetRepository
{
    public DataSetRepository(InsightEngineContext context) : base(context)
    {
    }

    public async Task<DataSet?> GetByStoredFileNameAsync(string storedFileName)
    {
        return await _dbSet
            .FirstOrDefaultAsync(ds => ds.StoredFileName == storedFileName);
    }

    public async Task<IReadOnlyList<DataSet>> GetExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(ds => ds.LastAccessedAt <= cutoffUtc)
            .ToListAsync(cancellationToken);
    }
}
