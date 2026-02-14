using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler para executar recomendação de gráfico e retornar EChartsOption completo
/// </summary>
public class GetDataSetChartQueryHandler : IRequestHandler<GetDataSetChartQuery, Result<EChartsOption>>
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

    public async Task<Result<EChartsOption>> Handle(GetDataSetChartQuery request, CancellationToken cancellationToken)
    {
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
                return Result<EChartsOption>.Failure<EChartsOption>($"Dataset not found: {request.DatasetId}");
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
                return Result<EChartsOption>.Failure<EChartsOption>(
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
                return executionResult;
            }

            _logger.LogInformation(
                "Chart executed successfully: {DatasetId}/{RecommendationId}",
                request.DatasetId, request.RecommendationId);

            return executionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing chart query: {DatasetId}/{RecommendationId}",
                request.DatasetId, request.RecommendationId);
            return Result<EChartsOption>.Failure<EChartsOption>($"Error executing chart: {ex.Message}");
        }
    }
}
