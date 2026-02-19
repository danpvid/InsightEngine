namespace InsightEngine.Domain.ValueObjects;

public class DatasetProfile
{
    public Guid DatasetId { get; set; }
    public int RowCount { get; set; }
    public int SampleSize { get; set; }
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public bool SchemaConfirmed { get; set; }
    public List<ColumnProfile> Columns { get; set; } = new();
}
