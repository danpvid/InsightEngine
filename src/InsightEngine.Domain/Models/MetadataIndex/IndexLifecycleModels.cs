using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.MetadataIndex;

public class IndexBuildOptions
{
    public int MaxColumnsForCorrelation { get; set; } = 50;
    public int TopKEdgesPerColumn { get; set; } = 10;
    public int SampleRows { get; set; } = 50000;
    public bool IncludeStringPatterns { get; set; } = true;
    public bool IncludeDistributions { get; set; } = true;

    public int MaxColumnsIndexed { get; set; } = 200;
    public int TopValuesLimitPerColumn { get; set; } = 20;
    public int HistogramBins { get; set; } = 20;
}

public class DatasetIndexStatus
{
    public Guid DatasetId { get; set; }
    public IndexBuildState Status { get; set; } = IndexBuildState.NotBuilt;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? BuiltAtUtc { get; set; }
    public string? Message { get; set; }
    public string Version { get; set; } = "1.0";
}
