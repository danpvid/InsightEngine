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
}
