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
}
