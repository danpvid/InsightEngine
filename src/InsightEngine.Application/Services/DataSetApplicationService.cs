using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
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

    public DataSetApplicationService(
        IMediator mediator,
        ILogger<DataSetApplicationService> logger)
    {
        _mediator = mediator;
        _logger = logger;
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

        var query = new GetDataSetProfileQuery(datasetId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Profile generated for dataset {DatasetId}: {RowCount} rows, {ColumnCount} columns",
                datasetId, result.Data.RowCount, result.Data.Columns.Count);
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

        var query = new GetDataSetRecommendationsQuery(datasetId);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Generated {Count} recommendations for dataset {DatasetId}", 
                result.Data.Count, datasetId);
        }
        else
        {
            _logger.LogWarning("Failed to generate recommendations for dataset {DatasetId}: {Errors}", 
                datasetId, string.Join(", ", result.Errors));
        }

        return result;
    }
}
