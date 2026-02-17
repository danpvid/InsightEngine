using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class DataSetCleanupService : IDataSetCleanupService
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IMetadataCacheService _metadataCacheService;
    private readonly IChartQueryCache _chartQueryCache;
    private readonly IIndexStore _indexStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DataSetCleanupService> _logger;

    public DataSetCleanupService(
        IDataSetRepository dataSetRepository,
        IFileStorageService fileStorageService,
        IMetadataCacheService metadataCacheService,
        IChartQueryCache chartQueryCache,
        IIndexStore indexStore,
        IUnitOfWork unitOfWork,
        ILogger<DataSetCleanupService> logger)
    {
        _dataSetRepository = dataSetRepository;
        _fileStorageService = fileStorageService;
        _metadataCacheService = metadataCacheService;
        _chartQueryCache = chartQueryCache;
        _indexStore = indexStore;
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

            await _indexStore.InvalidateAsync(dataSet.Id, cancellationToken);

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

    public async Task<DataSetDeletionResult?> DeleteDatasetAsync(Guid datasetId, CancellationToken cancellationToken = default)
    {
        var dataSet = await _dataSetRepository.GetByIdAsync(datasetId);
        if (dataSet is null)
        {
            return null;
        }

        var deletedFile = false;
        if (!string.IsNullOrWhiteSpace(dataSet.StoredFileName))
        {
            deletedFile = await _fileStorageService.DeleteFileAsync(dataSet.StoredFileName);
        }

        if (!deletedFile && !string.IsNullOrWhiteSpace(dataSet.StoredPath))
        {
            deletedFile = DeleteIfExists(dataSet.StoredPath);
        }

        var legacyMetadataDeleted = false;
        var legacyMetadataPath = Path.Combine(_fileStorageService.GetStoragePath(), $"{datasetId}.meta.json");
        if (DeleteIfExists(legacyMetadataPath))
        {
            legacyMetadataDeleted = true;
        }

        await _metadataCacheService.ClearCacheAsync(datasetId);
        await _chartQueryCache.InvalidateDatasetAsync(datasetId);
        await _indexStore.InvalidateAsync(datasetId, cancellationToken);

        _dataSetRepository.Remove(dataSet);
        var commitSucceeded = await _unitOfWork.CommitAsync();

        _logger.LogInformation(
            "Dataset deleted. DatasetId={DatasetId} MetadataRemoved={MetadataRemoved} FileDeleted={FileDeleted} LegacyArtifactsDeleted={LegacyDeleted}",
            datasetId,
            commitSucceeded,
            deletedFile,
            legacyMetadataDeleted);

        return new DataSetDeletionResult
        {
            DatasetId = datasetId,
            RemovedMetadataRecord = commitSucceeded,
            DeletedFile = deletedFile,
            DeletedLegacyArtifacts = legacyMetadataDeleted,
            ClearedMetadataCache = true,
            ClearedChartCache = true
        };
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
