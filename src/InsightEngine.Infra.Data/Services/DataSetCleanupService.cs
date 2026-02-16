using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class DataSetCleanupService : IDataSetCleanupService
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DataSetCleanupService> _logger;

    public DataSetCleanupService(
        IDataSetRepository dataSetRepository,
        IFileStorageService fileStorageService,
        IUnitOfWork unitOfWork,
        ILogger<DataSetCleanupService> logger)
    {
        _dataSetRepository = dataSetRepository;
        _fileStorageService = fileStorageService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<DataSetCleanupResult> CleanupExpiredAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var effectiveRetentionDays = Math.Max(retentionDays, 0);
        var cutoffUtc = DateTime.UtcNow.AddDays(-effectiveRetentionDays);
        var expiredDataSets = await _dataSetRepository.GetExpiredAsync(cutoffUtc, cancellationToken);

        var deletedFiles = 0;
        var deletedLegacyArtifacts = 0;

        foreach (var dataSet in expiredDataSets)
        {
            if (DeleteIfExists(dataSet.StoredPath))
            {
                deletedFiles++;
            }

            var legacyMetadataPath = Path.Combine(_fileStorageService.GetStoragePath(), $"{dataSet.Id}.meta.json");
            if (DeleteIfExists(legacyMetadataPath))
            {
                deletedLegacyArtifacts++;
            }

            _dataSetRepository.Remove(dataSet);
        }

        var removedMetadataRecords = 0;
        if (expiredDataSets.Count > 0)
        {
            await _unitOfWork.CommitAsync();
            removedMetadataRecords = expiredDataSets.Count;
        }

        var result = new DataSetCleanupResult
        {
            CutoffUtc = cutoffUtc,
            ExpiredDatasets = expiredDataSets.Count,
            RemovedMetadataRecords = removedMetadataRecords,
            DeletedFiles = deletedFiles,
            DeletedLegacyArtifacts = deletedLegacyArtifacts
        };

        _logger.LogInformation(
            "Dataset cleanup finished. CutoffUtc={CutoffUtc} Expired={Expired} MetadataRemoved={MetadataRemoved} FilesDeleted={FilesDeleted} LegacyArtifactsDeleted={LegacyArtifactsDeleted}",
            result.CutoffUtc,
            result.ExpiredDatasets,
            result.RemovedMetadataRecords,
            result.DeletedFiles,
            result.DeletedLegacyArtifacts);

        return result;
    }

    private static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
