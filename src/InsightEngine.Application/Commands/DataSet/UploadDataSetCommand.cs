using InsightEngine.Application.Commands;

namespace InsightEngine.Application.Commands.DataSet;

public class UploadDataSetCommand : Command
{
    public Stream FileStream { get; set; }
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long FileSize { get; set; }

    public UploadDataSetCommand(Stream fileStream, string fileName, string contentType, long fileSize)
    {
        FileStream = fileStream;
        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
    }

    public override bool IsValid()
    {
        if (FileStream == null || FileSize == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            return false;
        }

        // Valida extensão CSV
        var extension = Path.GetExtension(FileName)?.ToLowerInvariant();
        if (extension != ".csv")
        {
            return false;
        }

        // Valida Content-Type
        if (!ContentType.Contains("text/csv", StringComparison.OrdinalIgnoreCase) &&
            !ContentType.Contains("application/csv", StringComparison.OrdinalIgnoreCase) &&
            !ContentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
