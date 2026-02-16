using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IChartQueryCache
{
    Task<ChartExecutionResponse?> GetAsync(
        Guid datasetId,
        string recommendationId,
        string queryHash);

    Task SetAsync(
        Guid datasetId,
        string recommendationId,
        string queryHash,
        ChartExecutionResponse response);

    Task InvalidateDatasetAsync(Guid datasetId);
}
