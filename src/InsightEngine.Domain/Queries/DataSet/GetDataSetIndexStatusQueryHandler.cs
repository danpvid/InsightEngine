using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexStatusQueryHandler : IRequestHandler<GetDataSetIndexStatusQuery, Result<DatasetIndexStatus>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexStore _indexStore;
    private readonly ICurrentUser _currentUser;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<GetDataSetIndexStatusQueryHandler> _logger;

    public GetDataSetIndexStatusQueryHandler(
        IDataSetRepository dataSetRepository,
        IIndexStore indexStore,
        ICurrentUser currentUser,
        InsightEngineFeatures features,
        ILogger<GetDataSetIndexStatusQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexStore = indexStore;
        _currentUser = currentUser;
        _features = features;
        _logger = logger;
    }

    public async Task<Result<DatasetIndexStatus>> Handle(
        GetDataSetIndexStatusQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
            {
                return Result.Failure<DatasetIndexStatus>("Unauthorized");
            }

            var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
                ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
                : await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<DatasetIndexStatus>("Dataset not found.");
            }

            var status = await _indexStore.LoadStatusAsync(request.DatasetId, cancellationToken);
            return Result.Success(status, "Dataset index status loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load index status for {DatasetId}", request.DatasetId);
            return Result.Failure<DatasetIndexStatus>($"Failed to load index status: {ex.Message}");
        }
    }
}
