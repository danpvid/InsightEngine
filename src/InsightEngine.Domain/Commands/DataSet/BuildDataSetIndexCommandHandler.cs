using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Commands.DataSet;

public class BuildDataSetIndexCommandHandler : IRequestHandler<BuildDataSetIndexCommand, Result<BuildDataSetIndexResponse>>
{
    private readonly IIndexingEngine _indexingEngine;
    private readonly ILogger<BuildDataSetIndexCommandHandler> _logger;

    public BuildDataSetIndexCommandHandler(
        IIndexingEngine indexingEngine,
        ILogger<BuildDataSetIndexCommandHandler> logger)
    {
        _indexingEngine = indexingEngine;
        _logger = logger;
    }

    public async Task<Result<BuildDataSetIndexResponse>> Handle(
        BuildDataSetIndexCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
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
