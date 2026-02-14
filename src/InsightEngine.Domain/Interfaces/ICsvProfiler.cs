using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Interfaces;

public interface ICsvProfiler
{
    Task<DatasetProfile> ProfileAsync(Guid datasetId, string filePath, CancellationToken cancellationToken = default);
}
