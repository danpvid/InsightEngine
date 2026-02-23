using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.ImportSchema;

public class DatasetImportSchema
{
    public Guid DatasetId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public bool SchemaConfirmed { get; set; } = true;
    public string? TargetColumn { get; set; }
    public string? UniqueKeyColumn { get; set; }
    public string CurrencyCode { get; set; } = "BRL";
    public DateTime FinalizedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DatasetImportSchemaColumn> Columns { get; set; } = new();
}

public class DatasetImportSchemaColumn
{
    public string Name { get; set; } = string.Empty;
    public InferredType InferredType { get; set; }
    public InferredType ConfirmedType { get; set; }
    public bool IsIgnored { get; set; }
    public bool IsTarget { get; set; }
    public string? CurrencyCode { get; set; }
    public bool? HasPercentSign { get; set; }
    public PercentageScaleHint? PercentageScaleHint { get; set; }
}
