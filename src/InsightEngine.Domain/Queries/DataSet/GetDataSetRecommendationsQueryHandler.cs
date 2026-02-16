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
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICsvProfiler _csvProfiler;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly ILogger<GetDataSetRecommendationsQueryHandler> _logger;

    public GetDataSetRecommendationsQueryHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        RecommendationEngine recommendationEngine,
        ILogger<GetDataSetRecommendationsQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
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

            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<List<ChartRecommendation>>("Dataset not found");
            }

            // Verify CSV file exists
            var csvPath = dataSet.StoredPath;
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
            dataSet.MarkAccessed();
            _dataSetRepository.Update(dataSet);
            await _unitOfWork.CommitAsync();

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
}
