using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Application.Models.DataSet;

public class BuildIndexRequest : InputModel
{
    public int MaxColumnsForCorrelation { get; set; } = 50;
    public int TopKEdgesPerColumn { get; set; } = 10;
    public int SampleRows { get; set; } = 50000;
    public bool IncludeStringPatterns { get; set; } = true;
    public bool IncludeDistributions { get; set; } = true;
}

public class BuildIndexResponse : OutputModel
{
    public Guid DatasetId { get; set; }
    public string Status { get; set; } = "ready";
    public DateTime BuiltAtUtc { get; set; }
    public IndexLimits LimitsUsed { get; set; } = new();
}

public class GetIndexResponse : OutputModel
{
    public Guid DatasetId { get; set; }
    public DatasetIndex? Index { get; set; }
}

public class FormulaDiscoveryRequest : InputModel
{
    public string? Target { get; set; }
    public int MaxCandidates { get; set; } = 3;
    public int SampleCap { get; set; } = 50000;
    public int TopKFeatures { get; set; } = 10;
    public bool EnableInteractions { get; set; } = true;
    public bool EnableRatios { get; set; } = false;
    public bool Force { get; set; } = false;
}
