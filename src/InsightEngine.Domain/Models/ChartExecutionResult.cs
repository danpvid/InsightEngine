namespace InsightEngine.Domain.Models;

/// <summary>
/// Resultado da execução de chart com telemetria
/// </summary>
public class ChartExecutionResult
{
    /// <summary>
    /// ECharts option gerado
    /// </summary>
    public EChartsOption Option { get; set; } = null!;

    /// <summary>
    /// Tempo de execução da query DuckDB em milissegundos
    /// </summary>
    public long DuckDbMs { get; set; }

    /// <summary>
    /// SQL gerado e executado
    /// </summary>
    public string GeneratedSql { get; set; } = string.Empty;

    /// <summary>
    /// Número de pontos retornados
    /// </summary>
    public int RowCount { get; set; }
}
