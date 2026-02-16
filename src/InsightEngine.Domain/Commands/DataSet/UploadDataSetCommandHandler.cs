using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using DataSetEntity = InsightEngine.Domain.Entities.DataSet;

namespace InsightEngine.Domain.Commands.DataSet;

/// <summary>
/// Handler for UploadDataSetCommand
/// Contains business logic for dataset upload
/// </summary>
public class UploadDataSetCommandHandler : IRequestHandler<UploadDataSetCommand, Result<UploadDataSetResponse>>
{
    private readonly IFileStorageService _fileStorageService;
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UploadDataSetCommandHandler> _logger;

    public UploadDataSetCommandHandler(
        IFileStorageService fileStorageService,
        IDataSetRepository dataSetRepository,
        IUnitOfWork unitOfWork,
        ILogger<UploadDataSetCommandHandler> logger)
    {
        _fileStorageService = fileStorageService;
        _dataSetRepository = dataSetRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<UploadDataSetResponse>> Handle(
        UploadDataSetCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing dataset upload: {FileName}, Size: {Size} bytes",
                request.File.FileName,
                request.File.Length);

            // Generate unique dataset ID
            var datasetId = Guid.NewGuid();
            var storedFileName = $"{datasetId}.csv";

            _logger.LogInformation(
                "Generated datasetId: {DatasetId}, storedFileName: {StoredFileName}",
                datasetId,
                storedFileName);

            // Save CSV file using streaming
            await using var fileStream = request.File.OpenReadStream();

            var (storedPath, fileSize) = await _fileStorageService.SaveFileAsync(
                fileStream: fileStream,
                fileName: storedFileName,
                cancellationToken: cancellationToken);

            var dataSet = new DataSetEntity(
                datasetId,
                request.File.FileName,
                storedFileName,
                storedPath,
                fileSize,
                request.File.ContentType ?? "text/csv");

            await _dataSetRepository.AddAsync(dataSet);
            await _unitOfWork.CommitAsync();

            _logger.LogInformation(
                "Dataset uploaded successfully: {DatasetId}, Path: {Path}",
                datasetId,
                storedPath);

            var response = new UploadDataSetResponse
            {
                DatasetId = datasetId,
                OriginalFileName = request.File.FileName,
                StoredFileName = storedFileName,
                SizeBytes = fileSize,
                CreatedAtUtc = DateTime.UtcNow
            };

            return Result.Success(response, "Dataset uploaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading dataset: {FileName}", request.File.FileName);
            return Result.Failure<UploadDataSetResponse>($"Error uploading dataset: {ex.Message}");
        }
    }
}
