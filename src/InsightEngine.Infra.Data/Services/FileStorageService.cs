using InsightEngine.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class FileStorageService : IFileStorageService
{
    private readonly string _storagePath;
    private readonly ILogger<FileStorageService> _logger;
    private const int BufferSize = 81920; // 80KB buffer para streaming eficiente

    public FileStorageService(IConfiguration configuration, ILogger<FileStorageService> logger)
    {
        _logger = logger;
        _storagePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        
        // Cria o diretório se não existir
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
            _logger.LogInformation("Storage directory created at: {Path}", _storagePath);
        }
    }

    public async Task<(string storedPath, long fileSize)> SaveFileAsync(
        Stream fileStream, 
        string fileName, 
        CancellationToken cancellationToken = default)
    {
        if (fileStream == null || !fileStream.CanRead)
        {
            throw new ArgumentException("Invalid file stream", nameof(fileStream));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty", nameof(fileName));
        }

        // Sanitizar o nome do arquivo para prevenir path traversal
        var sanitizedFileName = SanitizeFileName(fileName);
        var fullPath = GetFullPath(sanitizedFileName);

        try
        {
            _logger.LogInformation("Starting file save: {FileName} to {Path}", fileName, fullPath);

            long totalBytesRead = 0;

            // Usar FileStream com buffer otimizado para arquivos grandes
            await using (var fileOutputStream = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;

                // Streaming com buffer para não carregar arquivo inteiro na memória
                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileOutputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalBytesRead += bytesRead;

                    // Log a cada 10MB processados para arquivos grandes
                    if (totalBytesRead % (10 * 1024 * 1024) == 0)
                    {
                        _logger.LogDebug("Processed {Size} MB for file {FileName}", 
                            totalBytesRead / (1024 * 1024), fileName);
                    }
                }

                await fileOutputStream.FlushAsync(cancellationToken);
            }

            _logger.LogInformation("File saved successfully: {FileName}, Size: {Size} bytes", 
                fileName, totalBytesRead);

            return (fullPath, totalBytesRead);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving file: {FileName}", fileName);
            
            // Cleanup em caso de erro
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(deleteEx, "Failed to delete partial file: {Path}", fullPath);
                }
            }
            
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var fullPath = GetFullPath(sanitizedFileName);

        try
        {
            if (File.Exists(fullPath))
            {
                await Task.Run(() => File.Delete(fullPath));
                _logger.LogInformation("File deleted: {FileName}", fileName);
                return true;
            }

            _logger.LogWarning("File not found for deletion: {FileName}", fileName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file: {FileName}", fileName);
            return false;
        }
    }

    public async Task<Stream?> GetFileStreamAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var fullPath = GetFullPath(sanitizedFileName);

        try
        {
            if (File.Exists(fullPath))
            {
                // Retorna FileStream para leitura em streaming (não carrega tudo na memória)
                return await Task.Run(() => new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    useAsync: true));
            }

            _logger.LogWarning("File not found: {FileName}", fileName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file stream: {FileName}", fileName);
            return null;
        }
    }

    public Task<bool> FileExistsAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.FromResult(false);
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var fullPath = GetFullPath(sanitizedFileName);

        return Task.FromResult(File.Exists(fullPath));
    }

    public string GetFullPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty", nameof(fileName));
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        return Path.Combine(_storagePath, sanitizedFileName);
    }

    public string GetStoragePath()
    {
        return _storagePath;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty", nameof(fileName));
        }

        // Remove path traversal attempts e caracteres inválidos
        var sanitized = Path.GetFileName(fileName);
        
        // Remove caracteres inválidos adicionais
        var invalidChars = Path.GetInvalidFileNameChars();
        sanitized = string.Join("_", sanitized.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Previne nomes vazios ou apenas com espaços
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Invalid file name after sanitization", nameof(fileName));
        }

        return sanitized;
    }
}
