using System.Diagnostics;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler para executar recomendação de gráfico e retornar resposta completa com telemetria
/// </summary>
public class GetDataSetChartQueryHandler : IRequestHandler<GetDataSetChartQuery, Result<ChartExecutionResponse>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IChartExecutionService _chartExecutionService;
    private readonly ILogger<GetDataSetChartQueryHandler> _logger;

    public GetDataSetChartQueryHandler(
        IFileStorageService fileStorageService,
        ICsvProfiler csvProfiler,
        IChartExecutionService chartExecutionService,
        ILogger<GetDataSetChartQueryHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _csvProfiler = csvProfiler;
        _chartExecutionService = chartExecutionService;
        _logger = logger;
    }

    public async Task<Result<ChartExecutionResponse>> Handle(GetDataSetChartQuery request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "🚀 Executing chart - DatasetId: {DatasetId}, RecommendationId: {RecommendationId}, Agg: {Agg}, TimeBin: {TimeBin}, YCol: {YCol}",
            request.DatasetId, request.RecommendationId, request.Aggregation ?? "null", request.TimeBin ?? "null", request.YColumn ?? "null");

        try
        {           
            // 1. Validar existência do dataset
            var csvPath = _fileStorageService.GetFullPath($"{request.DatasetId}.csv");
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("Dataset not found: {DatasetId}", request.DatasetId);
                return Result.Failure<ChartExecutionResponse>($"Dataset not found: {request.DatasetId}");
            }

            // 2. Gerar profile (necessário para recommendations)
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

            _logger.LogInformation(
                "📋 Original recommendation - Agg: {OrigAgg}, TimeBin: {OrigTime}, YCol: {OrigY}",
                recommendation.Aggregation, recommendation.TimeBin, recommendation.YColumn);

            // 4.1. Aplicar overrides dos parâmetros (controles dinâmicos do frontend)
            if (!string.IsNullOrWhiteSpace(request.Aggregation) || 
                !string.IsNullOrWhiteSpace(request.TimeBin) || 
                !string.IsNullOrWhiteSpace(request.YColumn))
            {
                _logger.LogInformation("🔧 Applying dynamic overrides...");
                recommendation = ApplyDynamicOverrides(recommendation, request.Aggregation, request.TimeBin, request.YColumn);
                
                _logger.LogInformation(
                    "✅ After override - Agg: {NewAgg}, TimeBin: {NewTime}, YCol: {NewY}",
                    recommendation.Aggregation, recommendation.TimeBin, recommendation.YColumn);
            }
            else
            {
                _logger.LogInformation("ℹ️ No overrides requested, using original recommendation");
            }

            // 5. Executar a recomendação via DuckDB
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

            // 6. Calcular hash da query
            var queryHash = QueryHashHelper.ComputeQueryHash(recommendation, request.DatasetId);

            // 7. Montar resposta com telemetria
            var response = new ChartExecutionResponse
            {
                DatasetId = request.DatasetId,
                RecommendationId = request.RecommendationId,
                ExecutionResult = executionResult.Data!,
                TotalExecutionMs = sw.ElapsedMilliseconds,
                QueryHash = queryHash
            };

            _logger.LogInformation(
                "Chart executed successfully: {DatasetId}/{RecommendationId}, TotalMs: {TotalMs}, DuckDbMs: {DuckDbMs}, RowCount: {RowCount}, QueryHash: {QueryHash}",
                request.DatasetId, request.RecommendationId, response.TotalExecutionMs, 
                response.ExecutionResult.DuckDbMs, response.ExecutionResult.RowCount, queryHash);

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

    /// <summary>
    /// Aplica sobrescrições dinâmicas nos parâmetros da recomendação
    /// Criado para suportar os controles de exploração do frontend (P1)
    /// </summary>
    private ChartRecommendation ApplyDynamicOverrides(
        ChartRecommendation original, 
        string? aggregation, 
        string? timeBin, 
        string? yColumn)
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
            Series = original.Query.Series != null ? new FieldSpec
            {
                Column = original.Query.Series.Column,
                Role = original.Query.Series.Role,
                Aggregation = original.Query.Series.Aggregation,
                Bin = original.Query.Series.Bin
            } : null,
            TopN = original.Query.TopN
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
