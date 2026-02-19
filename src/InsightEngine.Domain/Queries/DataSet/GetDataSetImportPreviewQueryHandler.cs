using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.ImportPreview;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetImportPreviewQueryHandler : IRequestHandler<GetDataSetImportPreviewQuery, Result<ImportPreviewResponse>>
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly ICsvProfiler _csvProfiler;
    private readonly ILogger<GetDataSetImportPreviewQueryHandler> _logger;

    public GetDataSetImportPreviewQueryHandler(
        IDataSetRepository dataSetRepository,
        ICsvProfiler csvProfiler,
        ILogger<GetDataSetImportPreviewQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _csvProfiler = csvProfiler;
        _logger = logger;
    }

    public async Task<Result<ImportPreviewResponse>> Handle(
        GetDataSetImportPreviewQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var dataSet = await _dataSetRepository.GetByIdAsync(request.DatasetId);
            if (dataSet is null)
            {
                return Result.Failure<ImportPreviewResponse>("Dataset not found");
            }

            if (!File.Exists(dataSet.StoredPath))
            {
                _logger.LogError(
                    "File not found for dataset {DatasetId}: {Path}",
                    request.DatasetId,
                    dataSet.StoredPath);

                return Result.Failure<ImportPreviewResponse>("Dataset file not found in the system");
            }

            var preview = await _csvProfiler.AnalyzeSampleAsync(
                request.DatasetId,
                dataSet.StoredPath,
                request.SampleSize,
                cancellationToken);

            _logger.LogInformation(
                "Generated import preview for dataset {DatasetId}: sampleRows={SampleSize}, columns={Columns}",
                request.DatasetId,
                preview.SampleSize,
                preview.Columns.Count);

            return Result.Success(preview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating import preview for dataset {DatasetId}", request.DatasetId);
            return Result.Failure<ImportPreviewResponse>($"Error generating import preview: {ex.Message}");
        }
    }
}
