namespace InsightEngine.Application.Models.DataSet;

public class FinalizeImportRequest : InputModel
{
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public Dictionary<string, string> ColumnTypeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrencyCode { get; set; } = "BRL";
    public FinalizeImportFormulaInferenceOptions? FormulaInference { get; set; }
}

public class FinalizeImportFormulaInferenceOptions : InputModel
{
    public bool Enabled { get; set; }
    public int? MaxColumns { get; set; }
    public int? MaxDepth { get; set; }
    public double? EpsilonAbs { get; set; }
    public bool? IncludePercentageColumns { get; set; }
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
