using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Queries.DataSet;
using InsightEngine.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;

namespace InsightEngine.Application.Services;

/// <summary>
/// Application Service interface for DataSet operations
/// </summary>
public interface IDataSetApplicationService
{
    /// <summary>
    /// Upload a CSV dataset file
    /// </summary>
    Task<Result<UploadDataSetResponse>> UploadAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get profile analysis for a dataset
    /// </summary>
    Task<Result<DatasetProfile>> GetProfileAsync(Guid datasetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get chart recommendations for a dataset
    /// </summary>
    Task<Result<List<ChartRecommendation>>> GetRecommendationsAsync(Guid datasetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all datasets
    /// </summary>
    Task<Result<List<DataSetSummary>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a chart recommendation and get complete response with telemetry
    /// </summary>
    /// <param name="datasetId">Dataset ID</param>
    /// <param name="recommendationId">Recommendation ID</param>
    /// <param name="aggregation">Optional: Override aggregation (Sum, Avg, Count, Min, Max)</param>
    /// <param name="timeBin">Optional: Override time bin (Day, Week, Month, Quarter, Year)</param>
    /// <param name="yColumn">Optional: Override Y metric column</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Result<ChartExecutionResponse>> GetChartAsync(
        Guid datasetId, 
        string recommendationId, 
        string? aggregation = null,
        string? timeBin = null,
        string? yColumn = null,
        CancellationToken cancellationToken = default);
}
