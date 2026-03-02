using InsightEngine.Domain.Core;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Services;
using InsightEngine.Domain.Settings;
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
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IIndexStore _indexStore;
    private readonly ICurrentUser _currentUser;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly IRecommendationEngineV2 _recommendationEngineV2;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<GetDataSetRecommendationsQueryHandler> _logger;

    public GetDataSetRecommendationsQueryHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        IDataSetSchemaStore schemaStore,
        IIndexStore indexStore,
        ICurrentUser currentUser,
        RecommendationEngine recommendationEngine,
        IRecommendationEngineV2 recommendationEngineV2,
        InsightEngineFeatures features,
        ILogger<GetDataSetRecommendationsQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _csvProfiler = csvProfiler;
        _schemaStore = schemaStore;
        _indexStore = indexStore;
        _currentUser = currentUser;
        _recommendationEngine = recommendationEngine;
        _recommendationEngineV2 = recommendationEngineV2;
        _features = features;
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

            if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
            {
                return Result.Failure<List<ChartRecommendation>>("Unauthorized");
            }

            var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
                ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
                : await _dataSetRepository.GetByIdAsync(request.DatasetId);
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
            var schema = await _schemaStore.LoadAsync(request.DatasetId, cancellationToken);
            profile = DatasetSchemaProfileMapper.ApplySchema(profile, schema);

            // Generate recommendations
            List<ChartRecommendation> recommendations;
            if (_features.RecommendationV2Enabled)
            {
                DatasetIndex? index = await _indexStore.LoadAsync(request.DatasetId, cancellationToken);
                recommendations = _recommendationEngineV2.Generate(profile, index);
            }
            else
            {
                recommendations = _recommendationEngine.Generate(profile);
            }
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
