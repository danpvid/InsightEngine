namespace InsightEngine.Domain.Models;

public class ChartQuery
{
    public FieldSpec X { get; set; } = new();
    public FieldSpec Y { get; set; } = new();
    public FieldSpec? Series { get; set; }
    public int? TopN { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
}
