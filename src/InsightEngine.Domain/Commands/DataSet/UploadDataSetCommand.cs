using Microsoft.AspNetCore.Http;

namespace InsightEngine.Domain.Commands.DataSet;

/// <summary>
/// Command to upload a CSV dataset file
/// </summary>
public class UploadDataSetCommand : Command<UploadDataSetResponse>
{
    public IFormFile File { get; set; }
    public long MaxFileSizeBytes { get; set; }

    public UploadDataSetCommand(IFormFile file, long maxFileSizeBytes = 20 * 1024 * 1024)
    {
        File = file;
        MaxFileSizeBytes = maxFileSizeBytes;
    }
}

/// <summary>
/// Response for UploadDataSetCommand
/// </summary>
public class UploadDataSetResponse
{
    public Guid DatasetId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
