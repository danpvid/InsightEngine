using InsightEngine.Domain.Entities;

namespace InsightEngine.Domain.Interfaces;

public interface IDataSetRepository : IRepository<DataSet>
{
    Task<DataSet?> GetByIdForOwnerAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DataSet>> GetAllForOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<DataSet?> GetByStoredFileNameAsync(string storedFileName);
    Task<IReadOnlyList<DataSet>> GetExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
