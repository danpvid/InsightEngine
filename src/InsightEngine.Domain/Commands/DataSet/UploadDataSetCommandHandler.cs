using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Commands.DataSet;

/// <summary>
/// Handler for UploadDataSetCommand
/// Contains business logic for dataset upload
/// </summary>
public class UploadDataSetCommandHandler : IRequestHandler<UploadDataSetCommand, Result<UploadDataSetResponse>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<UploadDataSetCommandHandler> _logger;

    public UploadDataSetCommandHandler(
        IFileStorageService fileStorageService,
        ILogger<UploadDataSetCommandHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<Result<UploadDataSetResponse>> Handle(
        UploadDataSetCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing dataset upload: {FileName}, Size: {Size} bytes",
                request.File.FileName,
                request.File.Length);

            // Generate unique dataset ID
            var datasetId = Guid.NewGuid();
            var storedFileName = $"{datasetId}.csv";

            _logger.LogInformation(
                "Generated datasetId: {DatasetId}, storedFileName: {StoredFileName}",
                datasetId,
                storedFileName);

            // Save CSV file using streaming
            await using var fileStream = request.File.OpenReadStream();

            var (storedPath, fileSize) = await _fileStorageService.SaveFileAsync(
                fileStream: fileStream,
                fileName: storedFileName,
                cancellationToken: cancellationToken);

            // Create metadata
            var metadata = new
            {
                Id = datasetId,
                OriginalFileName = request.File.FileName,
                StoredFileName = storedFileName,
                StoredPath = storedPath,
                FileSizeInBytes = fileSize,
                CreatedAt = DateTime.UtcNow
            };

            // Save metadata as JSON
            var metadataPath = GetMetadataFilePath(datasetId);
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

            _logger.LogInformation(
                "Dataset uploaded successfully: {DatasetId}, Path: {Path}",
                datasetId,
                storedPath);

            var response = new UploadDataSetResponse
            {
                DatasetId = datasetId,
                OriginalFileName = request.File.FileName,
                StoredFileName = storedFileName,
                SizeBytes = fileSize,
                CreatedAtUtc = DateTime.UtcNow
            };

            return Result.Success(response, "Dataset uploaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading dataset: {FileName}", request.File.FileName);
            return Result.Failure<UploadDataSetResponse>($"Error uploading dataset: {ex.Message}");
        }
    }

    private string GetMetadataFilePath(Guid datasetId)
    {
        var directory = _fileStorageService.GetStoragePath();
        return Path.Combine(directory, $"{datasetId}.meta.json");
    }
}
