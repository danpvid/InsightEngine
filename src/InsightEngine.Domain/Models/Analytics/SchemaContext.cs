using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.Analytics;

public class SchemaContext
{
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public string CurrencyCode { get; set; } = "BRL";
    public List<SchemaContextColumn> Columns { get; set; } = new();
}

public class SchemaContextColumn
{
    public string Name { get; set; } = string.Empty;
    public InferredType ConfirmedType { get; set; }
    public bool IsIgnored { get; set; }
    public PercentageScaleHint? PercentageScaleHint { get; set; }
}
