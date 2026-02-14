namespace InsightEngine.Domain.Interfaces;

public interface IFileStorageService
{
    Task<(string storedPath, long fileSize)> SaveFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string fileName);
    Task<Stream?> GetFileStreamAsync(string fileName);
    Task<bool> FileExistsAsync(string fileName);
    string GetFullPath(string fileName);
    string GetStoragePath();
}
