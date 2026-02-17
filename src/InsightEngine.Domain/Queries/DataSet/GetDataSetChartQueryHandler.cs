using System;
using System.Diagnostics;
using System.Linq;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler para executar recomendação de gráfico e retornar resposta completa com telemetria
/// </summary>
public class GetDataSetChartQueryHandler : IRequestHandler<GetDataSetChartQuery, Result<ChartExecutionResponse>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IChartExecutionService _chartExecutionService;
    private readonly IChartPercentileService _chartPercentileService;
    private readonly IChartQueryCache _chartQueryCache;
    private readonly ILogger<GetDataSetChartQueryHandler> _logger;

    public GetDataSetChartQueryHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        IChartExecutionService chartExecutionService,
        IChartPercentileService chartPercentileService,
        IChartQueryCache chartQueryCache,
        ILogger<GetDataSetChartQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _csvProfiler = csvProfiler;
        _chartExecutionService = chartExecutionService;
        _chartPercentileService = chartPercentileService;
        _chartQueryCache = chartQueryCache;
        _logger = logger;
    }

    public async Task<Result<ChartExecutionResponse>> Handle(GetDataSetChartQuery request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Chart execution started DatasetId={DatasetId} RecommendationId={RecommendationId} Aggregation={Aggregation} TimeBin={TimeBin} YColumn={YColumn}",
            request.DatasetId, request.RecommendationId, request.Aggregation ?? "null", request.TimeBin ?? "null", request.YColumn ?? "null");

        try
        {
            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                _logger.LogWarning("Dataset not found: {DatasetId}", request.DatasetId);
                return Result.Failure<ChartExecutionResponse>($"Dataset not found: {request.DatasetId}");
            }

            var csvPath = dataSet.StoredPath;
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning(
                    "Dataset file not found DatasetId={DatasetId} StoredPath={StoredPath}",
                    request.DatasetId,
                    csvPath);
                return Result.Failure<ChartExecutionResponse>($"Dataset file not found: {request.DatasetId}");
            }

            // 2. Generate profile (required for recommendations)
            var profile = await _csvProfiler.ProfileAsync(request.DatasetId, csvPath, cancellationToken);

            // 3. Gerar recommendations (on-demand, sem persistência - MVP pattern)
            var engine = new Services.RecommendationEngine();
            var recommendations = engine.Generate(profile);

            // 4. Encontrar a recomendação solicitada
            var recommendation = recommendations.FirstOrDefault(r => r.Id == request.RecommendationId);
            if (recommendation == null)
            {
                _logger.LogWarning(
                    "Recommendation {RecommendationId} not found for dataset {DatasetId}",
                    request.RecommendationId, request.DatasetId);
                return Result.Failure<ChartExecutionResponse>(
                    $"Recommendation '{request.RecommendationId}' not found. Available recommendations: {string.Join(", ", recommendations.Select(r => r.Id))}");
            }

            var columnLookup = profile.Columns
                .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var invalidColumns = new List<string>();

            string? resolvedYColumn = null;
            if (!string.IsNullOrWhiteSpace(request.YColumn))
            {
                if (!columnLookup.TryGetValue(request.YColumn, out var yProfile))
                {
                    invalidColumns.Add(request.YColumn);
                }
                else
                {
                    resolvedYColumn = yProfile.Name;
                    if (yProfile.InferredType != InferredType.Number)
                    {
                        return Result.Failure<ChartExecutionResponse>(
                            $"YColumn '{resolvedYColumn}' must be numeric.");
                    }
                }
            }

            string? resolvedGroupBy = null;
            if (!string.IsNullOrWhiteSpace(request.GroupBy))
            {
                if (!columnLookup.TryGetValue(request.GroupBy, out var groupProfile))
                {
                    invalidColumns.Add(request.GroupBy);
                }
                else
                {
                    var maxDistinct = Math.Max(20, (int)(profile.SampleSize * 0.05));
                    if (groupProfile.DistinctCount > maxDistinct)
                    {
                        return Result.Failure<ChartExecutionResponse>(
                            $"GroupBy '{groupProfile.Name}' has high cardinality (distinct {groupProfile.DistinctCount}).");
                    }

                    resolvedGroupBy = groupProfile.Name;
                }
            }

            var resolvedFilters = new List<ChartFilter>();
            foreach (var filter in request.Filters)
            {
                if (!columnLookup.TryGetValue(filter.Column, out var filterProfile))
                {
                    invalidColumns.Add(filter.Column);
                    continue;
                }

                resolvedFilters.Add(new ChartFilter
                {
                    Column = filterProfile.Name,
                    Operator = filter.Operator,
                    Values = filter.Values,
                    LogicalOperator = filter.LogicalOperator
                });
            }

            if (invalidColumns.Count > 0)
            {
                return Result.Failure<ChartExecutionResponse>(
                    $"Invalid column(s): {string.Join(", ", invalidColumns.Distinct(StringComparer.OrdinalIgnoreCase))}");
            }

            _logger.LogInformation(
                "📋 Original recommendation - Agg: {OrigAgg}, TimeBin: {OrigTime}, YCol: {OrigY}",
                recommendation.Aggregation, recommendation.TimeBin, recommendation.YColumn);

            // 4.1. Aplicar overrides dos parâmetros (controles dinâmicos do frontend)
            if (!string.IsNullOrWhiteSpace(request.Aggregation) || 
                !string.IsNullOrWhiteSpace(request.TimeBin) || 
                !string.IsNullOrWhiteSpace(request.YColumn) ||
                !string.IsNullOrWhiteSpace(request.GroupBy) ||
                resolvedFilters.Count > 0)
            {
                _logger.LogInformation("🔧 Applying dynamic overrides...");
                recommendation = ApplyDynamicOverrides(
                    recommendation,
                    request.Aggregation,
                    request.TimeBin,
                    resolvedYColumn,
                    resolvedGroupBy,
                    resolvedFilters);
                
                _logger.LogInformation(
                    "✅ After override - Agg: {NewAgg}, TimeBin: {NewTime}, YCol: {NewY}",
                    recommendation.Aggregation, recommendation.TimeBin, recommendation.YColumn);
            }
            else
            {
                _logger.LogInformation("ℹ️ No overrides requested, using original recommendation");
            }

            // 5. Executar a recomendação via DuckDB
            var viewFingerprint = $"{request.View}|{request.PercentileMode}|{request.PercentileKind}|{request.PercentileTarget?.Trim().ToLowerInvariant()}";
            var queryHash = QueryHashHelper.ComputeQueryHash(recommendation, request.DatasetId, viewFingerprint);

            var cachedResponse = await _chartQueryCache.GetAsync(
                request.DatasetId,
                request.RecommendationId,
                queryHash);

            if (cachedResponse != null)
            {
                sw.Stop();
                cachedResponse.TotalExecutionMs = sw.ElapsedMilliseconds;
                cachedResponse.CacheHit = true;

                _logger.LogInformation(
                    "Chart execution completed DatasetId={DatasetId} RecommendationId={RecommendationId} QueryHash={QueryHash} DuckDbMs={DuckDbMs} RowCountReturned={RowCountReturned} CacheHit={CacheHit}",
                    request.DatasetId,
                    request.RecommendationId,
                    queryHash,
                    cachedResponse.ExecutionResult.DuckDbMs,
                    cachedResponse.ExecutionResult.RowCount,
                    true);

                await TouchDatasetAsync(dataSet);

                return Result.Success(cachedResponse);
            }

            var executionResult = await _chartExecutionService.ExecuteAsync(
                request.DatasetId,
                recommendation,
                cancellationToken);

            if (!executionResult.IsSuccess)
            {
                _logger.LogError(
                    "Failed to execute chart {RecommendationId} for dataset {DatasetId}",
                    request.RecommendationId, request.DatasetId);
                return Result.Failure<ChartExecutionResponse>(executionResult.Errors);
            }

            sw.Stop();

            var percentileComputation = await _chartPercentileService.ComputeAsync(
                csvPath,
                recommendation,
                executionResult.Data!.Option,
                request.View,
                request.PercentileMode,
                request.PercentileKind,
                request.PercentileTarget ?? "y",
                cancellationToken);

            ChartPercentileMeta percentilesMeta = new()
            {
                Supported = false,
                Mode = PercentileMode.NotApplicable,
                Available = new List<PercentileKind>
                {
                    PercentileKind.P5,
                    PercentileKind.P10,
                    PercentileKind.P90,
                    PercentileKind.P95
                },
                Reason = "Percentile metadata unavailable."
            };
            ChartViewMeta viewMeta = new()
            {
                Kind = ChartViewKind.Base
            };

            if (percentileComputation.IsSuccess && percentileComputation.Data != null)
            {
                percentilesMeta = percentileComputation.Data.Percentiles;
                viewMeta = percentileComputation.Data.View;
                if (percentileComputation.Data.Option != null)
                {
                    executionResult.Data!.Option = percentileComputation.Data.Option;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Percentile computation failed DatasetId={DatasetId} RecommendationId={RecommendationId}: {Errors}",
                    request.DatasetId,
                    request.RecommendationId,
                    string.Join(", ", percentileComputation.Errors));
            }

            InsightSummary? insightSummary = null;
            try
            {
                insightSummary = InsightSummaryBuilder.Build(recommendation, executionResult.Data!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build insight summary for {RecommendationId}", request.RecommendationId);
            }

            // 7. Montar resposta com telemetria
            var response = new ChartExecutionResponse
            {
                DatasetId = request.DatasetId,
                RecommendationId = request.RecommendationId,
                ExecutionResult = executionResult.Data!,
                InsightSummary = insightSummary,
                Percentiles = percentilesMeta,
                View = viewMeta,
                TotalExecutionMs = sw.ElapsedMilliseconds,
                QueryHash = queryHash,
                CacheHit = false
            };

            await _chartQueryCache.SetAsync(
                request.DatasetId,
                request.RecommendationId,
                queryHash,
                response);

            _logger.LogInformation(
                "Chart execution completed DatasetId={DatasetId} RecommendationId={RecommendationId} QueryHash={QueryHash} DuckDbMs={DuckDbMs} RowCountReturned={RowCountReturned} CacheHit={CacheHit} TotalMs={TotalMs}",
                request.DatasetId,
                request.RecommendationId,
                queryHash,
                response.ExecutionResult.DuckDbMs,
                response.ExecutionResult.RowCount,
                false,
                response.TotalExecutionMs);

            await TouchDatasetAsync(dataSet);

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error executing chart query: {DatasetId}/{RecommendationId}",
                request.DatasetId, request.RecommendationId);
            return Result.Failure<ChartExecutionResponse>($"Error executing chart: {ex.Message}");
        }
    }

    private async Task TouchDatasetAsync(Domain.Entities.DataSet dataSet)
    {
        dataSet.MarkAccessed();
        _dataSetRepository.Update(dataSet);
        await _unitOfWork.CommitAsync();
    }

    /// <summary>
    /// Aplica sobrescrições dinâmicas nos parâmetros da recomendação
    /// Criado para suportar os controles de exploração do frontend (P1)
    /// </summary>
    private ChartRecommendation ApplyDynamicOverrides(
        ChartRecommendation original, 
        string? aggregation, 
        string? timeBin, 
        string? yColumn,
        string? groupBy,
        List<ChartFilter> filters)
    {
        _logger.LogInformation("🔍 ApplyDynamicOverrides called - Input: Agg={Agg}, TimeBin={TimeBin}, YCol={YCol}",
            aggregation ?? "null", timeBin ?? "null", yColumn ?? "null");

        // Parse dos overrides para enums
        Aggregation? aggEnum = null;
        if (!string.IsNullOrWhiteSpace(aggregation))
        {
            if (Enum.TryParse<Aggregation>(aggregation, true, out var parsedAgg))
            {
                aggEnum = parsedAgg;
                _logger.LogInformation("✅ Parsed Aggregation: '{Input}' → {Enum}", aggregation, aggEnum);
            }
            else
            {
                _logger.LogWarning("❌ Failed to parse Aggregation: '{Input}'", aggregation);
            }
        }

        TimeBin? binEnum = null;
        if (!string.IsNullOrWhiteSpace(timeBin))
        {
            if (Enum.TryParse<TimeBin>(timeBin, true, out var parsedBin))
            {
                binEnum = parsedBin;
                _logger.LogInformation("✅ Parsed TimeBin: '{Input}' → {Enum}", timeBin, binEnum);
            }
            else
            {
                _logger.LogWarning("❌ Failed to parse TimeBin: '{Input}'", timeBin);
            }
        }

        var seriesSpec = original.Query.Series != null ? new FieldSpec
        {
            Column = original.Query.Series.Column,
            Role = original.Query.Series.Role,
            Aggregation = original.Query.Series.Aggregation,
            Bin = original.Query.Series.Bin
        } : null;

        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            seriesSpec = new FieldSpec
            {
                Column = groupBy,
                Role = AxisRole.Category
            };
        }

        // Clone do Query com overrides
        var newQuery = new ChartQuery
        {
            X = new FieldSpec
            {
                Column = original.Query.X.Column,
                Role = original.Query.X.Role,
                Aggregation = original.Query.X.Aggregation,
                Bin = binEnum ?? original.Query.X.Bin  // Apply override se presente
            },
            Y = new FieldSpec
            {
                Column = !string.IsNullOrWhiteSpace(yColumn) ? yColumn : original.Query.Y.Column,
                Role = original.Query.Y.Role,
                Aggregation = aggEnum ?? original.Query.Y.Aggregation,  // Apply override se presente
                Bin = original.Query.Y.Bin
            },
            Series = seriesSpec,
            TopN = original.Query.TopN,
            Filters = filters.Count > 0 ? filters : original.Query.Filters
        };

        // Clone do ChartRecommendation completo
        var overridden = new ChartRecommendation
        {
            Id = original.Id,
            Title = original.Title,
            Reason = $"{original.Reason} [Modified by user]",
            Chart = original.Chart,
            Query = newQuery,
            OptionTemplate = original.OptionTemplate
        };

        _logger.LogInformation(
            "🎯 Final Query values - X.Bin: {Bin}, Y.Aggregation: {Agg}, Y.Column: {YCol}",
            overridden.Query.X.Bin, overridden.Query.Y.Aggregation, overridden.Query.Y.Column);

        return overridden;
    }
}
