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
}

/// <summary>
/// Metadados de telemetria da execução do chart
/// </summary>
public class ChartExecutionMeta
{
    /// <summary>
    /// Tempo total de execução em milissegundos (handler completo)
    /// </summary>
    public long ExecutionMs { get; set; }

    /// <summary>
    /// Tempo de execução da query DuckDB em milissegundos
    /// </summary>
    public long DuckDbMs { get; set; }

    /// <summary>
    /// Hash SHA256 da query spec para cache/deduplicação
    /// </summary>
    public string QueryHash { get; set; } = string.Empty;

    /// <summary>
    /// Versão do dataset (null se não versionado ainda)
    /// </summary>
    public string? DatasetVersion { get; set; }

    /// <summary>
    /// Número de linhas retornadas pela query
    /// </summary>
    public int RowCountReturned { get; set; }

    /// <summary>
    /// Tipo do gráfico executado
    /// </summary>
    public string ChartType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp UTC de geração do gráfico (ISO-8601 com Z)
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// SQL gerado (apenas em Development)
    /// </summary>
    public string? DebugSql { get; set; }
}
