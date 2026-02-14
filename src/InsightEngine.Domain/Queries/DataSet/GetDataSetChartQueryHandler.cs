using System.Diagnostics;
using InsightEngine.Domain.Core;
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
            "Executing chart for dataset {DatasetId}, recommendation {RecommendationId}",
            request.DatasetId, request.RecommendationId);

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
}
