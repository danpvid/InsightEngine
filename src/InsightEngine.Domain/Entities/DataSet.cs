namespace InsightEngine.Domain.Entities;

public class DataSet : Core.Models.Entity
{
    public string OriginalFileName { get; private set; } = string.Empty;
    public string StoredFileName { get; private set; } = string.Empty;
    public string StoredPath { get; private set; } = string.Empty;
    public long FileSizeInBytes { get; private set; }
    public string ContentType { get; private set; } = "text/csv";
    public long? RowCount { get; private set; }
    public string? ProfileSummary { get; private set; }
    public DateTime LastAccessedAt { get; private set; }

    protected DataSet() { }

    public DataSet(
        Guid id,
        string originalFileName,
        string storedFileName,
        string storedPath,
        long fileSizeInBytes,
        string contentType)
    {
        Id = id;
        OriginalFileName = originalFileName;
        StoredFileName = storedFileName;
        StoredPath = storedPath;
        FileSizeInBytes = fileSizeInBytes;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "text/csv" : contentType;
        LastAccessedAt = DateTime.UtcNow;
    }

    public void UpdateFileInfo(long fileSizeInBytes)
    {
        FileSizeInBytes = fileSizeInBytes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProfile(long rowCount, string? profileSummary)
    {
        RowCount = rowCount;
        ProfileSummary = profileSummary;
        LastAccessedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAccessed()
    {
        LastAccessedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
