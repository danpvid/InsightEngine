namespace InsightEngine.Domain.Models;

public class DataSetDeletionResult
{
    public Guid DatasetId { get; set; }
    public bool RemovedMetadataRecord { get; set; }
    public bool DeletedFile { get; set; }
    public bool DeletedLegacyArtifacts { get; set; }
    public bool ClearedMetadataCache { get; set; }
    public bool ClearedChartCache { get; set; }
}
