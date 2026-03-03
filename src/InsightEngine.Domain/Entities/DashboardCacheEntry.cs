using InsightEngine.Domain.Core.Models;

namespace InsightEngine.Domain.Entities;

public class DashboardCacheEntry : Entity
{
    public Guid OwnerUserId { get; private set; }
    public Guid DatasetId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTime SourceDatasetUpdatedAt { get; private set; }
    public string SourceFingerprint { get; private set; } = string.Empty;

    protected DashboardCacheEntry()
    {
    }

    public DashboardCacheEntry(
        Guid ownerUserId,
        Guid datasetId,
        string version,
        string payloadJson,
        DateTime sourceDatasetUpdatedAt,
        string sourceFingerprint)
    {
        OwnerUserId = ownerUserId;
        DatasetId = datasetId;
        Version = version;
        PayloadJson = payloadJson;
        SourceDatasetUpdatedAt = sourceDatasetUpdatedAt;
        SourceFingerprint = sourceFingerprint;
    }

    public void UpdatePayload(
        string payloadJson,
        DateTime sourceDatasetUpdatedAt,
        string sourceFingerprint)
    {
        PayloadJson = payloadJson;
        SourceDatasetUpdatedAt = sourceDatasetUpdatedAt;
        SourceFingerprint = sourceFingerprint;
        UpdatedAt = DateTime.UtcNow;
    }
}
