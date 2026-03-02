using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Commands.DataSet;

public class BuildDataSetIndexCommandHandler : IRequestHandler<BuildDataSetIndexCommand, Result<BuildDataSetIndexResponse>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexingEngine _indexingEngine;
    private readonly ICurrentUser _currentUser;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<BuildDataSetIndexCommandHandler> _logger;

    public BuildDataSetIndexCommandHandler(
        IDataSetRepository dataSetRepository,
        IIndexingEngine indexingEngine,
        ICurrentUser currentUser,
        InsightEngineFeatures features,
        ILogger<BuildDataSetIndexCommandHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexingEngine = indexingEngine;
        _currentUser = currentUser;
        _features = features;
        _logger = logger;
    }

    public async Task<Result<BuildDataSetIndexResponse>> Handle(
        BuildDataSetIndexCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
            {
                return Result.Failure<BuildDataSetIndexResponse>("Unauthorized");
            }

            var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
                ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
                : await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<BuildDataSetIndexResponse>("Dataset not found.");
            }

            var index = await _indexingEngine.BuildAsync(
                request.DatasetId,
                new IndexBuildOptions
                {
                    MaxColumnsForCorrelation = request.MaxColumnsForCorrelation,
                    TopKEdgesPerColumn = request.TopKEdgesPerColumn,
                    SampleRows = request.SampleRows,
                    IncludeStringPatterns = request.IncludeStringPatterns,
                    IncludeDistributions = request.IncludeDistributions
                },
                cancellationToken);

            return Result.Success(new BuildDataSetIndexResponse
            {
                DatasetId = request.DatasetId,
                Status = Domain.Enums.IndexBuildState.Ready,
                BuiltAtUtc = index.BuiltAtUtc,
                LimitsUsed = index.Limits
            }, "Dataset index built successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build dataset index for {DatasetId}", request.DatasetId);
            return Result.Failure<BuildDataSetIndexResponse>($"Failed to build dataset index: {ex.Message}");
        }
    }
}
