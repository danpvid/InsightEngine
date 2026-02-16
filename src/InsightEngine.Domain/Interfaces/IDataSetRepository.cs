using InsightEngine.Domain.Entities;

namespace InsightEngine.Domain.Interfaces;

public interface IDataSetRepository : IRepository<DataSet>
{
    Task<DataSet?> GetByStoredFileNameAsync(string storedFileName);
    Task<IReadOnlyList<DataSet>> GetExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
