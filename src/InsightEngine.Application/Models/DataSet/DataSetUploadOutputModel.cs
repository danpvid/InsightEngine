namespace InsightEngine.Application.Models.DataSet;

public class DataSetUploadOutputModel : OutputModel
{
    public Guid DataSetId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public DateTime CreatedAt { get; set; }

    public DataSetUploadOutputModel()
    {
    }

    public DataSetUploadOutputModel(
        Guid dataSetId,
        string originalFileName,
        string storedPath,
        long fileSizeInBytes,
        DateTime createdAt)
    {
        Success = true;
        DataSetId = dataSetId;
        OriginalFileName = originalFileName;
        StoredPath = storedPath;
        FileSizeInBytes = fileSizeInBytes;
        CreatedAt = createdAt;
    }
}
