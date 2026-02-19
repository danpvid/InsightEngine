namespace InsightEngine.Domain.Commands.DataSet;

public class FinalizeDataSetImportCommand : Command<FinalizeDataSetImportResponse>
{
    public Guid DatasetId { get; }
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public Dictionary<string, string> ColumnTypeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrencyCode { get; set; } = "BRL";

    public FinalizeDataSetImportCommand(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}

public class FinalizeDataSetImportResponse
{
    public Guid DatasetId { get; set; }
    public int SchemaVersion { get; set; }
    public string? TargetColumn { get; set; }
    public int IgnoredColumnsCount { get; set; }
    public int StoredColumnsCount { get; set; }
    public string CurrencyCode { get; set; } = "BRL";
}
