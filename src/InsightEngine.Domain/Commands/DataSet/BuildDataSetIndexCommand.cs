using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Commands.DataSet;

public class BuildDataSetIndexCommand : Command<BuildDataSetIndexResponse>
{
    public Guid DatasetId { get; set; }
    public int MaxColumnsForCorrelation { get; set; } = 50;
    public int TopKEdgesPerColumn { get; set; } = 10;
    public int SampleRows { get; set; } = 50000;
    public bool IncludeStringPatterns { get; set; } = true;
    public bool IncludeDistributions { get; set; } = true;

    public BuildDataSetIndexCommand(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}

public class BuildDataSetIndexResponse
{
    public Guid DatasetId { get; set; }
    public IndexBuildState Status { get; set; } = IndexBuildState.Ready;
    public DateTime BuiltAtUtc { get; set; }
    public IndexLimits LimitsUsed { get; set; } = new();
}
