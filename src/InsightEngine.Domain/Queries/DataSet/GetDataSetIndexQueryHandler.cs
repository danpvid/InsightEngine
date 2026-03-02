using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexQueryHandler : IRequestHandler<GetDataSetIndexQuery, Result<DatasetIndex>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexStore _indexStore;
    private readonly ICurrentUser _currentUser;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<GetDataSetIndexQueryHandler> _logger;

    public GetDataSetIndexQueryHandler(
        IDataSetRepository dataSetRepository,
        IIndexStore indexStore,
        ICurrentUser currentUser,
        InsightEngineFeatures features,
        ILogger<GetDataSetIndexQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexStore = indexStore;
        _currentUser = currentUser;
        _features = features;
        _logger = logger;
    }

    public async Task<Result<DatasetIndex>> Handle(
        GetDataSetIndexQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_features.AuthRequiredForDatasets && (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue))
            {
                return Result.Failure<DatasetIndex>("Unauthorized");
            }

            var dataSet = _currentUser.IsAuthenticated && _currentUser.UserId.HasValue
                ? await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken)
                : await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<DatasetIndex>("Dataset not found.");
            }

            var index = await _indexStore.LoadAsync(request.DatasetId, cancellationToken);
            if (index == null)
            {
                return Result.Failure<DatasetIndex>("Dataset index not found.");
            }

            return Result.Success(index, "Dataset index loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dataset index for {DatasetId}", request.DatasetId);
            return Result.Failure<DatasetIndex>($"Failed to load dataset index: {ex.Message}");
        }
    }
}
