using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler for GetDataSetProfileQuery
/// </summary>
public class GetDataSetProfileQueryHandler : IRequestHandler<GetDataSetProfileQuery, Result<DatasetProfile>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ICsvProfiler _csvProfiler;
    private readonly ILogger<GetDataSetProfileQueryHandler> _logger;

    public GetDataSetProfileQueryHandler(
        IFileStorageService fileStorageService,
        ICsvProfiler csvProfiler,
        ILogger<GetDataSetProfileQueryHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _csvProfiler = csvProfiler;
        _logger = logger;
    }

    public async Task<Result<DatasetProfile>> Handle(
        GetDataSetProfileQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Generating profile for dataset {DatasetId}", request.DatasetId);

            // Load metadata
            var metadata = await LoadMetadataAsync(request.DatasetId);
            if (metadata == null)
            {
                return Result.Failure<DatasetProfile>("Dataset not found");
            }

            // Verify CSV file exists
            if (!File.Exists(metadata.StoredPath))
            {
                _logger.LogError(
                    "File not found for dataset {DatasetId}: {Path}",
                    request.DatasetId,
                    metadata.StoredPath);
                
                return Result.Failure<DatasetProfile>("Dataset file not found in the system");
            }

            // Generate profile
            var profile = await _csvProfiler.ProfileAsync(request.DatasetId, metadata.StoredPath);

            _logger.LogInformation(
                "Profile generated for dataset {DatasetId}: {RowCount} rows, {ColumnCount} columns",
                request.DatasetId,
                profile.RowCount,
                profile.Columns.Count);

            return Result.Success(profile, "Profile generated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating profile for dataset {DatasetId}", request.DatasetId);
            return Result.Failure<DatasetProfile>($"Error generating profile: {ex.Message}");
        }
    }

    private async Task<DatasetMetadata?> LoadMetadataAsync(Guid datasetId)
    {
        var metadataPath = GetMetadataFilePath(datasetId);

        if (!File.Exists(metadataPath))
        {
            _logger.LogWarning("Metadata not found: {MetadataPath}", metadataPath);
            return null;
        }

        var json = await File.ReadAllTextAsync(metadataPath);
        return System.Text.Json.JsonSerializer.Deserialize<DatasetMetadata>(json);
    }

    private string GetMetadataFilePath(Guid datasetId)
    {
        var directory = _fileStorageService.GetStoragePath();
        return Path.Combine(directory, $"{datasetId}.meta.json");
    }

    private class DatasetMetadata
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public long FileSizeInBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
