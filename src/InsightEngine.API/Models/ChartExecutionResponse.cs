using InsightEngine.Domain.Models;

namespace InsightEngine.API.Models;

/// <summary>
/// Resposta do endpoint de execução de chart com telemetria
/// </summary>
public class ChartExecutionResponse
{
    /// <summary>
    /// ID do dataset
    /// </summary>
    public Guid DatasetId { get; set; }

    /// <summary>
    /// ID da recomendação executada
    /// </summary>
    public string RecommendationId { get; set; } = string.Empty;

    /// <summary>
    /// ECharts option completo pronto para setOption()
    /// </summary>
    public EChartsOption Option { get; set; } = null!;

    /// <summary>
    /// Metadados de execução e telemetria
    /// </summary>
    public ChartExecutionMeta Meta { get; set; } = new();

    /// <summary>
    /// SQL gerado (apenas em Development)
    /// </summary>
    public string? DebugSql { get; set; }
}
