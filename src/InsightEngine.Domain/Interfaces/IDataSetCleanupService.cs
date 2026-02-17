using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IDataSetCleanupService
{
    Task<DataSetCleanupResult> CleanupExpiredAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task<DataSetDeletionResult?> DeleteDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
