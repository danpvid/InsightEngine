using System.Text.Json;

namespace InsightEngine.Domain.Models;

public class ChartRecommendation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ChartMeta Chart { get; set; } = new();
    public ChartQuery Query { get; set; } = new();
    public Dictionary<string, object> OptionTemplate { get; set; } = new();
}
