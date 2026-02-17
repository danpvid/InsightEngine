using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.FormulaDiscovery;
using InsightEngine.Domain.Models.MetadataIndex;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaDiscoveryService : IFormulaDiscoveryService
{
    private const string DefaultIndexVersion = "metadata-index/v1";

    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexStore _indexStore;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IDuckDbMetadataAnalyzer _metadataAnalyzer;
    private readonly FeatureSelector _featureSelector;
    private readonly FormulaSamplingService _samplingService;
    private readonly FormulaCandidateRankingService _candidateRankingService;
    private readonly ILogger<FormulaDiscoveryService> _logger;

    public FormulaDiscoveryService(
        IDataSetRepository dataSetRepository,
        IIndexStore indexStore,
        ICsvProfiler csvProfiler,
        IDuckDbMetadataAnalyzer metadataAnalyzer,
        FeatureSelector featureSelector,
        FormulaSamplingService samplingService,
        FormulaCandidateRankingService candidateRankingService,
        ILogger<FormulaDiscoveryService> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexStore = indexStore;
        _csvProfiler = csvProfiler;
        _metadataAnalyzer = metadataAnalyzer;
        _featureSelector = featureSelector;
        _samplingService = samplingService;
        _candidateRankingService = candidateRankingService;
        _logger = logger;
    }

    public async Task<FormulaDiscoveryResult> DiscoverAsync(
        Guid datasetId,
        string targetColumn,
        FormulaDiscoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetColumn))
        {
            throw new ArgumentException("Target column is required.", nameof(targetColumn));
        }

        var boundedOptions = NormalizeOptions(options);
        var dataSet = await _dataSetRepository.GetByIdAsync(datasetId);
        if (dataSet is null)
        {
            throw new InvalidOperationException($"Dataset not found: {datasetId}");
        }

        if (!File.Exists(dataSet.StoredPath))
        {
            throw new FileNotFoundException("Dataset CSV file not found.", dataSet.StoredPath);
        }

        var datasetIndex = await _indexStore.LoadAsync(datasetId, cancellationToken);
        var rowCount = datasetIndex?.RowCount ?? 0;
        var columns = datasetIndex?.Columns ?? new List<ColumnIndex>();

        if (columns.Count == 0 || rowCount <= 0)
        {
            var profile = await _csvProfiler.ProfileAsync(datasetId, dataSet.StoredPath, cancellationToken);
            rowCount = profile.RowCount;
            columns = await _metadataAnalyzer.ComputeColumnProfilesAsync(
                dataSet.StoredPath,
                maxColumns: 200,
                topValuesLimit: 20,
                sampleRows: boundedOptions.SampleCap,
                cancellationToken: cancellationToken);
        }

        var target = columns.FirstOrDefault(column =>
            string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException($"Target column '{targetColumn}' was not found.");
        }

        if (target.InferredType != InferredType.Number)
        {
            throw new InvalidOperationException($"Target column '{targetColumn}' must be numeric.");
        }

        var cacheKey = BuildCacheKey(target.Name, boundedOptions);
        if (!boundedOptions.ForceRecompute
            && datasetIndex?.FormulaDiscovery is { } cached
            && string.Equals(cached.CacheKey, cacheKey, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Formula discovery cache hit for dataset {DatasetId} target {Target}.",
                datasetId,
                target.Name);
            return cached.Result;
        }

        var featureSelection = await _featureSelector.SelectTopFeaturesAsync(
            datasetId,
            target.Name,
            columns,
            rowCount,
            topK: boundedOptions.TopKFeatures,
            sampleCap: boundedOptions.SampleCap,
            cancellationToken: cancellationToken);

        if (featureSelection.SelectedFeatures.Count == 0)
        {
            var emptyResult = new FormulaDiscoveryResult
            {
                DatasetId = datasetId,
                TargetColumn = target.Name,
                GeneratedAt = DateTimeOffset.UtcNow,
                Candidates = new List<FormulaCandidate>(),
                ConsideredColumns = featureSelection.CandidateFeatures.ToList(),
                ExcludedColumns = featureSelection.ExcludedFeatures.ToList(),
                Notes = new List<string>
                {
                    "No eligible numeric drivers were found for formula inference.",
                    "This is a best-fit analysis and does not guarantee the true business formula."
                }
            };

            await PersistResultAsync(datasetId, rowCount, columns, cacheKey, emptyResult, cancellationToken);
            return emptyResult;
        }

        var sample = await _samplingService.LoadSampleAsync(
            datasetId,
            target.Name,
            featureSelection.SelectedFeatures,
            sampleCap: boundedOptions.SampleCap,
            cancellationToken: cancellationToken);

        var candidates = _candidateRankingService.BuildCandidates(
            sample,
            maxCandidates: boundedOptions.MaxCandidates,
            enableInteractions: boundedOptions.EnableInteractions,
            enableRatios: boundedOptions.EnableRatios);

        var result = new FormulaDiscoveryResult
        {
            DatasetId = datasetId,
            TargetColumn = target.Name,
            GeneratedAt = DateTimeOffset.UtcNow,
            Candidates = candidates.ToList(),
            ConsideredColumns = featureSelection.SelectedFeatures.ToList(),
            ExcludedColumns = featureSelection.ExcludedFeatures.ToList(),
            Notes = new List<string>
            {
                "This is a best-fit equation analysis and may not represent the true business logic.",
                "High explainability does not imply causality."
            }
        };

        await PersistResultAsync(datasetId, rowCount, columns, cacheKey, result, cancellationToken);
        return result;
    }

    private async Task PersistResultAsync(
        Guid datasetId,
        long rowCount,
        IReadOnlyList<ColumnIndex> columns,
        string cacheKey,
        FormulaDiscoveryResult result,
        CancellationToken cancellationToken)
    {
        var index = await _indexStore.LoadAsync(datasetId, cancellationToken);
        if (index is null)
        {
            index = new DatasetIndex
            {
                DatasetId = datasetId,
                BuiltAtUtc = DateTime.UtcNow,
                Version = DefaultIndexVersion,
                RowCount = rowCount,
                ColumnCount = columns.Count,
                Columns = columns.ToList()
            };
        }

        index.FormulaDiscovery = new FormulaDiscoveryIndexEntry
        {
            CacheKey = cacheKey,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Result = result
        };

        await _indexStore.SaveAsync(index, cancellationToken);
        _logger.LogInformation(
            "Formula discovery persisted for dataset {DatasetId} target {Target} (candidates: {Candidates}).",
            datasetId,
            result.TargetColumn,
            result.Candidates.Count);
    }

    private static FormulaDiscoveryOptions NormalizeOptions(FormulaDiscoveryOptions options)
    {
        return new FormulaDiscoveryOptions
        {
            MaxCandidates = Math.Clamp(options.MaxCandidates, 1, 5),
            SampleCap = Math.Clamp(options.SampleCap, 1_000, 100_000),
            TopKFeatures = Math.Clamp(options.TopKFeatures, 3, 20),
            EnableInteractions = options.EnableInteractions,
            EnableRatios = options.EnableRatios,
            ForceRecompute = options.ForceRecompute
        };
    }

    private static string BuildCacheKey(string targetColumn, FormulaDiscoveryOptions options)
    {
        return string.Join("|",
            targetColumn.Trim().ToLowerInvariant(),
            options.MaxCandidates,
            options.SampleCap,
            options.TopKFeatures,
            options.EnableInteractions ? "1" : "0",
            options.EnableRatios ? "1" : "0");
    }
}
