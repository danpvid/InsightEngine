using InsightEngine.Domain.Models.ImportPreview;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Interfaces;

public interface ICsvProfiler
{
    Task<DatasetProfile> ProfileAsync(Guid datasetId, string filePath, CancellationToken cancellationToken = default);
    Task<ImportPreviewResponse> AnalyzeSampleAsync(
        Guid datasetId,
        string filePath,
        int sampleSize = 200,
        CancellationToken cancellationToken = default);
}
