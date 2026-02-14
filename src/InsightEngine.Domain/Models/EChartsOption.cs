namespace InsightEngine.Domain.Models;

/// <summary>
/// Modelo para ECharts Option completo
/// Suporta serialização para JSON compatível com ECharts API
/// </summary>
public class EChartsOption
{
    public Dictionary<string, object>? Title { get; set; }
    public Dictionary<string, object>? Tooltip { get; set; }
    public Dictionary<string, object>? Legend { get; set; }
    public Dictionary<string, object>? Grid { get; set; }
    public Dictionary<string, object>? XAxis { get; set; }
    public Dictionary<string, object>? YAxis { get; set; }
    public List<Dictionary<string, object>>? Series { get; set; }
    public List<Dictionary<string, object>>? DataZoom { get; set; }
    public Dictionary<string, object>? ToolBox { get; set; }

    public EChartsOption()
    {
        Series = new List<Dictionary<string, object>>();
    }
}
