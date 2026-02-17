using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Interfaces;

public interface IIndexStore
{
    Task SaveAsync(DatasetIndex index, CancellationToken cancellationToken = default);
    Task<DatasetIndex?> LoadAsync(Guid datasetId, CancellationToken cancellationToken = default);

    Task SaveStatusAsync(DatasetIndexStatus status, CancellationToken cancellationToken = default);
    Task<DatasetIndexStatus> LoadStatusAsync(Guid datasetId, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid datasetId, CancellationToken cancellationToken = default);
}
