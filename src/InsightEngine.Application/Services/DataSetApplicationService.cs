using InsightEngine.Domain.Commands.DataSet;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.FormulaDiscovery;
using InsightEngine.Domain.Models.ImportPreview;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Queries.DataSet;
using InsightEngine.Domain.ValueObjects;
using InsightEngine.Application.Models.DataSet;
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
    private readonly IScenarioSimulationService _scenarioSimulationService;
    private readonly IDataSetSchemaStore _schemaStore;

    public DataSetApplicationService(
        IMediator mediator,
        ILogger<DataSetApplicationService> logger,
        IMetadataCacheService cacheService,
        IScenarioSimulationService scenarioSimulationService,
        IDataSetSchemaStore schemaStore)
    {
        _mediator = mediator;
        _logger = logger;
        _cacheService = cacheService;
        _scenarioSimulationService = scenarioSimulationService;
        _schemaStore = schemaStore;
    }

    public async Task<Result<UploadDataSetResponse>> UploadAsync(
        IFormFile file,
        long? maxFileSizeBytes = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Uploading file: {FileName}, Size: {Size} bytes", file.FileName, file.Length);

        var command = maxFileSizeBytes.HasValue
            ? new UploadDataSetCommand(file, maxFileSizeBytes.Value)
            : new UploadDataSetCommand(file);
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

    public async Task<Result<ImportPreviewResponse>> GetImportPreviewAsync(
        Guid datasetId,
        int sampleSize = 200,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating import preview for dataset {DatasetId} using sample size {SampleSize}",
            datasetId,
            sampleSize);

        var query = new GetDataSetImportPreviewQuery(datasetId, sampleSize);
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Import preview generated for dataset {DatasetId}: sampleRows={SampleRows}, columns={Columns}",
                datasetId,
                result.Data.SampleSize,
                result.Data.Columns.Count);
        }
        else
        {
            _logger.LogWarning(
                "Failed to generate import preview for dataset {DatasetId}: {Errors}",
                datasetId,
                string.Join(", ", result.Errors));
        }

        return result;
    }

    public async Task<Result<FinalizeImportResponse>> FinalizeImportAsync(
        Guid datasetId,
        FinalizeImportRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Finalizing import for dataset {DatasetId}: target={TargetColumn}, ignoredColumns={IgnoredColumnsCount}",
            datasetId,
            request.TargetColumn,
            request.IgnoredColumns.Count);

        var command = new FinalizeDataSetImportCommand(datasetId)
        {
            TargetColumn = request.TargetColumn,
            IgnoredColumns = request.IgnoredColumns ?? new List<string>(),
            ColumnTypeOverrides = request.ColumnTypeOverrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "BRL" : request.CurrencyCode
        };

        var commandResult = await _mediator.Send(command, cancellationToken);
        if (!commandResult.IsSuccess)
        {
            return Result.Failure<FinalizeImportResponse>(commandResult.Errors);
        }

        var data = commandResult.Data!;
        return Result.Success(new FinalizeImportResponse
        {
            DatasetId = data.DatasetId,
            SchemaVersion = data.SchemaVersion,
            TargetColumn = data.TargetColumn,
            IgnoredColumnsCount = data.IgnoredColumnsCount,
            StoredColumnsCount = data.StoredColumnsCount,
            CurrencyCode = data.CurrencyCode
        });
    }

    public async Task<Result<DatasetImportSchema>> GetSchemaAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var schema = await _schemaStore.LoadAsync(datasetId, cancellationToken);
        if (schema is not null)
        {
            return Result.Success(schema);
        }

        var profileResult = await GetProfileAsync(datasetId, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            return Result.Failure<DatasetImportSchema>(profileResult.Errors);
        }

        var backfilledSchema = BuildDefaultSchemaFromProfile(datasetId, profileResult.Data!);
        await _schemaStore.SaveAsync(backfilledSchema, cancellationToken);

        _logger.LogInformation(
            "Backfilled default schema for legacy dataset {DatasetId} with {ColumnCount} columns",
            datasetId,
            backfilledSchema.Columns.Count);

        return Result.Success(backfilledSchema);
    }

    private static DatasetImportSchema BuildDefaultSchemaFromProfile(Guid datasetId, DatasetProfile profile)
    {
        var columns = profile.Columns
            .Select(column =>
            {
                var inferredType = column.InferredType.NormalizeLegacy();
                var confirmedType = (column.ConfirmedType ?? column.InferredType).NormalizeLegacy();

                return new DatasetImportSchemaColumn
                {
                    Name = column.Name,
                    InferredType = inferredType,
                    ConfirmedType = confirmedType,
                    IsIgnored = false,
                    IsTarget = false,
                    CurrencyCode = confirmedType == InferredType.Money ? "BRL" : null,
                    HasPercentSign = confirmedType == InferredType.Percentage ? column.HasPercentSign : null
                };
            })
            .ToList();

        var targetColumn = columns
            .FirstOrDefault(column => column.ConfirmedType.IsNumericLike())
            ?.Name;

        if (!string.IsNullOrWhiteSpace(targetColumn))
        {
            foreach (var column in columns)
            {
                column.IsTarget = string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase);
            }
        }

        return new DatasetImportSchema
        {
            DatasetId = datasetId,
            SchemaVersion = 1,
            SchemaConfirmed = false,
            TargetColumn = targetColumn,
            CurrencyCode = "BRL",
            FinalizedAtUtc = DateTime.UtcNow,
            Columns = columns
        };
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
        string? groupBy = null,
        List<ChartFilter>? filters = null,
        ChartViewKind view = ChartViewKind.Base,
        PercentileMode percentileMode = PercentileMode.None,
        PercentileKind? percentileKind = null,
        string? percentileTarget = null,
        string? xColumn = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing chart for dataset {DatasetId}, recommendation {RecommendationId}, aggregation: {Aggregation}, timeBin: {TimeBin}, xColumn: {XColumn}, yColumn: {YColumn}", 
            datasetId, recommendationId, aggregation, timeBin, xColumn, yColumn);

        var query = new GetDataSetChartQuery(
            datasetId,
            recommendationId,
            aggregation,
            timeBin,
            yColumn,
            groupBy,
            filters,
            view,
            percentileMode,
            percentileKind,
            percentileTarget,
            xColumn);
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

    public async Task<Result<ScenarioSimulationResponse>> SimulateAsync(
        Guid datasetId,
        ScenarioRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Running scenario simulation for dataset {DatasetId} with {OperationCount} operation(s)",
            datasetId,
            request.Operations?.Count ?? 0);

        var profileResult = await GetProfileAsync(datasetId, cancellationToken);
        if (!profileResult.IsSuccess)
        {
            return Result.Failure<ScenarioSimulationResponse>(profileResult.Errors);
        }

        var simulationResult = await _scenarioSimulationService.SimulateAsync(
            datasetId,
            profileResult.Data!,
            request,
            cancellationToken);

        if (!simulationResult.IsSuccess)
        {
            _logger.LogWarning(
                "Scenario simulation failed for dataset {DatasetId}: {Errors}",
                datasetId,
                string.Join(", ", simulationResult.Errors));
            return simulationResult;
        }

        _logger.LogInformation(
            "Scenario simulation completed for dataset {DatasetId}. Rows={Rows}, DuckDbMs={DuckDbMs}",
            datasetId,
            simulationResult.Data!.RowCountReturned,
            simulationResult.Data.DuckDbMs);

        return simulationResult;
    }

    public async Task<Result<BuildDataSetIndexResponse>> BuildIndexAsync(
        Guid datasetId,
        BuildIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new BuildDataSetIndexCommand(datasetId)
        {
            MaxColumnsForCorrelation = request.MaxColumnsForCorrelation,
            TopKEdgesPerColumn = request.TopKEdgesPerColumn,
            SampleRows = request.SampleRows,
            IncludeStringPatterns = request.IncludeStringPatterns,
            IncludeDistributions = request.IncludeDistributions
        };

        return await _mediator.Send(command, cancellationToken);
    }

    public async Task<Result<DatasetIndex>> GetIndexAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var query = new GetDataSetIndexQuery(datasetId);
        return await _mediator.Send(query, cancellationToken);
    }

    public async Task<Result<DatasetIndexStatus>> GetIndexStatusAsync(
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var query = new GetDataSetIndexStatusQuery(datasetId);
        return await _mediator.Send(query, cancellationToken);
    }

    public async Task<Result<FormulaDiscoveryResult>> GetFormulaDiscoveryAsync(
        Guid datasetId,
        FormulaDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new GetDataSetFormulaDiscoveryQuery
        {
            DatasetId = datasetId,
            Target = request.Target,
            MaxCandidates = request.MaxCandidates,
            SampleCap = request.SampleCap,
            TopKFeatures = request.TopKFeatures,
            EnableInteractions = request.EnableInteractions,
            EnableRatios = request.EnableRatios,
            ForceRecompute = request.Force
        };

        return await _mediator.Send(query, cancellationToken);
    }
}
