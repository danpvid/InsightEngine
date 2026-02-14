using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler for GetDataSetRecommendationsQuery
/// </summary>
public class GetDataSetRecommendationsQueryHandler : IRequestHandler<GetDataSetRecommendationsQuery, Result<List<ChartRecommendation>>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ICsvProfiler _csvProfiler;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly ILogger<GetDataSetRecommendationsQueryHandler> _logger;

    public GetDataSetRecommendationsQueryHandler(
        IFileStorageService fileStorageService,
        ICsvProfiler csvProfiler,
        RecommendationEngine recommendationEngine,
        ILogger<GetDataSetRecommendationsQueryHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _csvProfiler = csvProfiler;
        _recommendationEngine = recommendationEngine;
        _logger = logger;
    }

    public async Task<Result<List<ChartRecommendation>>> Handle(
        GetDataSetRecommendationsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Generating chart recommendations for dataset {DatasetId}",
                request.DatasetId);

            // Load metadata
            var metadata = await LoadMetadataAsync(request.DatasetId);
            if (metadata == null)
            {
                return Result.Failure<List<ChartRecommendation>>("Dataset not found");
            }

            // Verify CSV file exists
            var csvPath = metadata.StoredPath;
            if (!File.Exists(csvPath))
            {
                _logger.LogError(
                    "File not found for dataset {DatasetId}: {Path}",
                    request.DatasetId,
                    csvPath);
                
                return Result.Failure<List<ChartRecommendation>>("Dataset file not found in the system");
            }

            // Generate profile first
            var profile = await _csvProfiler.ProfileAsync(request.DatasetId, csvPath);

            // Generate recommendations
            var recommendations = _recommendationEngine.Generate(profile);

            _logger.LogInformation(
                "Generated {Count} recommendations for dataset {DatasetId}",
                recommendations.Count,
                request.DatasetId);

            return Result.Success(recommendations, "Recommendations generated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error generating recommendations for dataset {DatasetId}",
                request.DatasetId);
            
            return Result.Failure<List<ChartRecommendation>>($"Error generating recommendations: {ex.Message}");
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
