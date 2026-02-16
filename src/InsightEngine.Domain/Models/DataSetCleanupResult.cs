namespace InsightEngine.Domain.Models;

public class DataSetCleanupResult
{
    public DateTime CutoffUtc { get; set; }
    public int ExpiredDatasets { get; set; }
    public int RemovedMetadataRecords { get; set; }
    public int DeletedFiles { get; set; }
    public int DeletedLegacyArtifacts { get; set; }
}
