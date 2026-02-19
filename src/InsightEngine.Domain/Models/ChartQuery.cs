namespace InsightEngine.Domain.Models;

public class ChartQuery
{
    public FieldSpec X { get; set; } = new();
    public FieldSpec Y { get; set; } = new();
    public List<FieldSpec> YMetrics { get; set; } = new();
    public FieldSpec? Series { get; set; }
    public Dictionary<string, int> YAxisMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int? TopN { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
}
