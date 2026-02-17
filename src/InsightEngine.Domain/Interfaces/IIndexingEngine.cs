using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Interfaces;

public interface IIndexingEngine
{
    Task<DatasetIndex> BuildAsync(
        Guid datasetId,
        IndexBuildOptions options,
        CancellationToken cancellationToken = default);
}
