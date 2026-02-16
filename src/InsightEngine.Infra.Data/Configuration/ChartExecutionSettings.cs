using InsightEngine.Domain.Enums;

namespace InsightEngine.Infra.Data.Configuration;

/// <summary>
/// Configurações para execução de charts
/// </summary>
public class ChartExecutionSettings
{
    /// <summary>
    /// Modo de preenchimento de lacunas em séries temporais
    /// </summary>
    public GapFillMode GapFillMode { get; set; } = GapFillMode.Nulls;

    /// <summary>
    /// Habilita DataZoom automático quando rowCount > threshold
    /// </summary>
    public bool EnableAutoDataZoom { get; set; } = true;

    /// <summary>
    /// Threshold de pontos para habilitar DataZoom automaticamente
    /// </summary>
    public int DataZoomThreshold { get; set; } = 200;

    /// <summary>
    /// Incluir SQL de debug na resposta (apenas Development)
    /// </summary>
    public bool IncludeDebugSql { get; set; } = false;

    /// <summary>
    /// Número máximo de categorias em gráfico de barras (TopN)
    /// </summary>
    public int BarChartTopN { get; set; } = 20;

    /// <summary>
    /// Número máximo de pontos em gráfico de dispersão (sampling)
    /// </summary>
    public int ScatterMaxPoints { get; set; } = 2000;

    public int TimeSeriesMaxPoints { get; set; } = 2000;

    /// <summary>
    /// Número de bins para histograma (default)
    /// </summary>
    public int HistogramBins { get; set; } = 20;

    /// <summary>
    /// Número mínimo de bins para histograma (Task 6.5)
    /// </summary>
    public int HistogramMinBins { get; set; } = 5;

    /// <summary>
    /// Número máximo de bins para histograma (Task 6.5)
    /// </summary>
    public int HistogramMaxBins { get; set; } = 50;
}
