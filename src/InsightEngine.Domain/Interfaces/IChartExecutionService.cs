using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

/// <summary>
/// Serviço responsável por executar recomendações de gráficos e retornar EChartsOption completo com dados reais
/// </summary>
public interface IChartExecutionService
{
    /// <summary>
    /// Executa uma recomendação de gráfico contra o dataset
    /// </summary>
    /// <param name="datasetId">ID do dataset</param>
    /// <param name="recommendation">Recomendação a ser executada</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result com EChartsOption completo incluindo dados</returns>
    Task<Result<EChartsOption>> ExecuteAsync(
        Guid datasetId,
        ChartRecommendation recommendation,
        CancellationToken ct = default);
}
