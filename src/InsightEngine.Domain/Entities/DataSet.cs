namespace InsightEngine.Domain.Entities;

public class DataSet : Core.Models.Entity
{
    public string OriginalFileName { get; private set; }
    public string StoredFileName { get; private set; }
    public string StoredPath { get; private set; }
    public long FileSizeInBytes { get; private set; }
    public string ContentType { get; private set; }

    protected DataSet() { }

    public DataSet(string originalFileName, string storedFileName, string storedPath, long fileSizeInBytes, string contentType)
    {
        OriginalFileName = originalFileName;
        StoredFileName = storedFileName;
        StoredPath = storedPath;
        FileSizeInBytes = fileSizeInBytes;
        ContentType = contentType;
    }

    public void UpdateFileInfo(long fileSizeInBytes)
    {
        FileSizeInBytes = fileSizeInBytes;
        UpdatedAt = DateTime.UtcNow;
    }
}
