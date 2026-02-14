using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler for GetAllDataSetsQuery - retrieves all dataset metadata
/// </summary>
public class GetAllDataSetsQueryHandler : IRequestHandler<GetAllDataSetsQuery, Result<List<DataSetSummary>>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<GetAllDataSetsQueryHandler> _logger;

    public GetAllDataSetsQueryHandler(
        IFileStorageService fileStorageService,
        ILogger<GetAllDataSetsQueryHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<Result<List<DataSetSummary>>> Handle(GetAllDataSetsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving all datasets");

            var directory = _fileStorageService.GetStoragePath();

            if (!Directory.Exists(directory))
            {
                _logger.LogInformation("Storage directory does not exist, returning empty list");
                return Result<List<DataSetSummary>>.Success(new List<DataSetSummary>());
            }

            var metadataFiles = Directory.GetFiles(directory, "*.meta.json");
            var datasets = new List<DataSetSummary>();

            foreach (var file in metadataFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var metadata = JsonSerializer.Deserialize<DataSetMetadata>(json);

                    if (metadata != null)
                    {
                        datasets.Add(new DataSetSummary
                        {
                            DatasetId = metadata.Id,
                            OriginalFileName = metadata.OriginalFileName,
                            StoredFileName = metadata.StoredFileName,
                            FileSizeInBytes = metadata.FileSizeInBytes,
                            FileSizeMB = Math.Round(metadata.FileSizeInBytes / (1024.0 * 1024.0), 2),
                            CreatedAt = metadata.CreatedAt
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load metadata from {File}", file);
                    // Continue processing other files
                }
            }

            _logger.LogInformation("Retrieved {Count} datasets", datasets.Count);

            return Result<List<DataSetSummary>>.Success(datasets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving datasets");
            return Result.Failure<List<DataSetSummary>>("Error retrieving datasets: " + ex.Message);
        }
    }

    // Internal class to deserialize metadata JSON
    private class DataSetMetadata
    {
        public Guid Id { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public long FileSizeInBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
