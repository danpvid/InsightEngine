using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexStatusQueryHandler : IRequestHandler<GetDataSetIndexStatusQuery, Result<DatasetIndexStatus>>
{
    private readonly IIndexStore _indexStore;
    private readonly ILogger<GetDataSetIndexStatusQueryHandler> _logger;

    public GetDataSetIndexStatusQueryHandler(
        IIndexStore indexStore,
        ILogger<GetDataSetIndexStatusQueryHandler> logger)
    {
        _indexStore = indexStore;
        _logger = logger;
    }

    public async Task<Result<DatasetIndexStatus>> Handle(
        GetDataSetIndexStatusQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
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
