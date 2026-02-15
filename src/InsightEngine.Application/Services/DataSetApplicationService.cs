using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Queries.DataSet;
using InsightEngine.Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Application.Services;

/// <summary>
/// Application Service for DataSet operations.
/// Thin orchestration layer that delegates to Domain Commands/Queries via MediatR.
/// </summary>
public class DataSetApplicationService : IDataSetApplicationService
{
    private readonly IMediator _mediator;
    private readonly ILogger<DataSetApplicationService> _logger;
    private readonly IMetadataCacheService _cacheService;

    public DataSetApplicationService(
        IMediator mediator,
        ILogger<DataSetApplicationService> logger,
        IMetadataCacheService cacheService)
    {
        _mediator = mediator;
        _logger = logger;
        _cacheService = cacheService;
    }

    public async Task<Result<UploadDataSetResponse>> UploadAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading file: {FileName}, Size: {Size} bytes", file.FileName, file.Length);

        var command = new UploadDataSetCommand(file);
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Dataset uploaded successfully: {DatasetId}", result.Data.DatasetId);
        }
        else
        {
            _logger.LogWarning("Failed to upload dataset: {Errors}", string.Join(", ", result.Errors));
        }

        return result;
    }

    public async Task<Result<DatasetProfile>> GetProfileAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating profile for dataset {DatasetId}", datasetId);

        // Task 6.4: Check cache first
        var cachedProfile = await _cacheService.GetCachedProfileAsync<DatasetProfile>(datasetId);
        if (cachedProfile != null)
        {
            _logger.LogInformation("Returning cached profile for dataset {DatasetId}", datasetId);
            return Result<DatasetProfile>.Success(cachedProfile);
        }

        var query = new GetDataSetProfileQuery(datasetId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Profile generated for dataset {DatasetId}: {RowCount} rows, {ColumnCount} columns",
                datasetId, result.Data.RowCount, result.Data.Columns.Count);
            
            // Cache the profile for future requests
            await _cacheService.SetCachedProfileAsync(datasetId, result.Data);
        }
        else
        {
            _logger.LogWarning("Failed to generate profile for dataset {DatasetId}: {Errors}", 
                datasetId, string.Join(", ", result.Errors));
        }

        return result;
    }

    public async Task<Result<List<ChartRecommendation>>> GetRecommendationsAsync(
        Guid datasetId, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating chart recommendations for dataset {DatasetId}", datasetId);

        // Task 6.4: Check cache first
        var cachedRecommendations = await _cacheService.GetCachedRecommendationsAsync<List<ChartRecommendation>>(datasetId);
        if (cachedRecommendations != null)
        {
            _logger.LogInformation("Returning cached recommendations for dataset {DatasetId}", datasetId);
            return Result<List<ChartRecommendation>>.Success(cachedRecommendations);
        }

        var query = new GetDataSetRecommendationsQuery(datasetId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated {Count} recommendations for dataset {DatasetId}", 
                result.Data.Count, datasetId);
            
            // Cache the recommendations for future requests
            await _cacheService.SetCachedRecommendationsAsync(datasetId, result.Data);
        }
        else
        {
            _logger.LogWarning("Failed to generate recommendations for dataset {DatasetId}: {Errors}", 
                datasetId, string.Join(", ", result.Errors));
        }

        return result;
    }

    public async Task<Result<List<DataSetSummary>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving all datasets");

        var query = new GetAllDataSetsQuery();
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Retrieved {Count} datasets", result.Data.Count);
        }
        else
        {
            _logger.LogWarning("Failed to retrieve datasets: {Errors}", string.Join(", ", result.Errors));
        }

        return result;
    }

    public async Task<Result<ChartExecutionResponse>> GetChartAsync(
        Guid datasetId, 
        string recommendationId,
        string? aggregation = null,
        string? timeBin = null,
        string? yColumn = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing chart for dataset {DatasetId}, recommendation {RecommendationId}, aggregation: {Aggregation}, timeBin: {TimeBin}, yColumn: {YColumn}", 
            datasetId, recommendationId, aggregation, timeBin, yColumn);

        var query = new GetDataSetChartQuery(datasetId, recommendationId, aggregation, timeBin, yColumn);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Chart executed successfully: {DatasetId}/{RecommendationId}, TotalMs: {TotalMs}",
                datasetId, recommendationId, result.Data!.TotalExecutionMs);
        }
        else
        {
            _logger.LogWarning(
                "Failed to execute chart {DatasetId}/{RecommendationId}: {Errors}",
                datasetId, recommendationId, string.Join(", ", result.Errors));
        }

        return result;
    }
}
