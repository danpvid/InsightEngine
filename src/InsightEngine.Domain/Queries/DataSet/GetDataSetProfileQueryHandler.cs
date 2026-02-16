using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Handler for GetDataSetProfileQuery
/// </summary>
public class GetDataSetProfileQueryHandler : IRequestHandler<GetDataSetProfileQuery, Result<DatasetProfile>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICsvProfiler _csvProfiler;
    private readonly ILogger<GetDataSetProfileQueryHandler> _logger;

    public GetDataSetProfileQueryHandler(
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ICsvProfiler csvProfiler,
        ILogger<GetDataSetProfileQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _csvProfiler = csvProfiler;
        _logger = logger;
    }

    public async Task<Result<DatasetProfile>> Handle(
        GetDataSetProfileQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Generating profile for dataset {DatasetId}", request.DatasetId);

            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<DatasetProfile>("Dataset not found");
            }

            // Verify CSV file exists
            if (!File.Exists(dataSet.StoredPath))
            {
                _logger.LogError(
                    "File not found for dataset {DatasetId}: {Path}",
                    request.DatasetId,
                    dataSet.StoredPath);
                
                return Result.Failure<DatasetProfile>("Dataset file not found in the system");
            }

            // Generate profile
            var profile = await _csvProfiler.ProfileAsync(request.DatasetId, dataSet.StoredPath);
            dataSet.UpdateProfile(
                profile.RowCount,
                $"Rows: {profile.RowCount}, Columns: {profile.Columns.Count}");
            _dataSetRepository.Update(dataSet);
            await _unitOfWork.CommitAsync();

            _logger.LogInformation(
                "Profile generated for dataset {DatasetId}: {RowCount} rows, {ColumnCount} columns",
                request.DatasetId,
                profile.RowCount,
                profile.Columns.Count);

            return Result.Success(profile, "Profile generated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating profile for dataset {DatasetId}", request.DatasetId);
            return Result.Failure<DatasetProfile>($"Error generating profile: {ex.Message}");
        }
    }
}
