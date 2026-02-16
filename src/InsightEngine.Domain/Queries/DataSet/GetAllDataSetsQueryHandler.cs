using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetAllDataSetsQueryHandler : IRequestHandler<GetAllDataSetsQuery, Result<List<DataSetSummary>>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly ILogger<GetAllDataSetsQueryHandler> _logger;

    public GetAllDataSetsQueryHandler(
        IDataSetRepository dataSetRepository,
        ILogger<GetAllDataSetsQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _logger = logger;
    }

    public async Task<Result<List<DataSetSummary>>> Handle(GetAllDataSetsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Retrieving all datasets from metadata store");

            var dataSets = await _dataSetRepository.GetAllAsync();
            var summaries = dataSets
                .OrderByDescending(dataset => dataset.CreatedAt)
                .Select(dataset => new DataSetSummary
                {
                    DatasetId = dataset.Id,
                    OriginalFileName = dataset.OriginalFileName,
                    StoredFileName = dataset.StoredFileName,
                    FileSizeInBytes = dataset.FileSizeInBytes,
                    FileSizeMB = Math.Round(dataset.FileSizeInBytes / (1024.0 * 1024.0), 2),
                    CreatedAt = dataset.CreatedAt,
                    RowCount = dataset.RowCount,
                    ProfileSummary = dataset.ProfileSummary,
                    LastAccessedAt = dataset.LastAccessedAt
                })
                .ToList();

            _logger.LogInformation("Retrieved {Count} dataset metadata record(s)", summaries.Count);

            return Result<List<DataSetSummary>>.Success(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dataset metadata");
            return Result.Failure<List<DataSetSummary>>("Error retrieving datasets: " + ex.Message);
        }
    }
}
