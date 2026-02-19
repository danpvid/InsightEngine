namespace InsightEngine.Application.Models.DataSet;

public class FinalizeImportRequest : InputModel
{
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public Dictionary<string, string> ColumnTypeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrencyCode { get; set; } = "BRL";
}

public class FinalizeImportResponse : OutputModel
{
    public Guid DatasetId { get; set; }
    public int SchemaVersion { get; set; }
    public string? TargetColumn { get; set; }
    public int IgnoredColumnsCount { get; set; }
    public int StoredColumnsCount { get; set; }
    public string CurrencyCode { get; set; } = "BRL";
}
