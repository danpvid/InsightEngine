using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexQueryHandler : IRequestHandler<GetDataSetIndexQuery, Result<DatasetIndex>>
{
    private readonly IIndexStore _indexStore;
    private readonly ILogger<GetDataSetIndexQueryHandler> _logger;

    public GetDataSetIndexQueryHandler(
        IIndexStore indexStore,
        ILogger<GetDataSetIndexQueryHandler> logger)
    {
        _indexStore = indexStore;
        _logger = logger;
    }

    public async Task<Result<DatasetIndex>> Handle(
        GetDataSetIndexQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
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
