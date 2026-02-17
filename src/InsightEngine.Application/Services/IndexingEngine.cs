using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Application.Services;

public class IndexingEngine : IIndexingEngine
{
    private const string IndexVersion = "metadata-index/v1";

    private readonly IDataSetRepository _dataSetRepository;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IDuckDbMetadataAnalyzer _metadataAnalyzer;
    private readonly ISemanticTagger _semanticTagger;
    private readonly IIndexStore _indexStore;
    private readonly ILogger<IndexingEngine> _logger;

    public IndexingEngine(
        IDataSetRepository dataSetRepository,
        ICsvProfiler csvProfiler,
        IDuckDbMetadataAnalyzer metadataAnalyzer,
        ISemanticTagger semanticTagger,
        IIndexStore indexStore,
        ILogger<IndexingEngine> logger)
    {
        _dataSetRepository = dataSetRepository;
        _csvProfiler = csvProfiler;
        _metadataAnalyzer = metadataAnalyzer;
        _semanticTagger = semanticTagger;
        _indexStore = indexStore;
        _logger = logger;
    }

    public async Task<DatasetIndex> BuildAsync(
        Guid datasetId,
        IndexBuildOptions options,
        CancellationToken cancellationToken = default)
    {
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

        await _indexStore.SaveStatusAsync(new DatasetIndexStatus
        {
            DatasetId = datasetId,
            Status = IndexBuildState.Building,
            Message = "Building metadata index...",
            Version = IndexVersion
        }, cancellationToken);

        try
        {
            var profile = await _csvProfiler.ProfileAsync(datasetId, dataSet.StoredPath, cancellationToken);

            var columns = await _metadataAnalyzer.ComputeColumnProfilesAsync(
                dataSet.StoredPath,
                boundedOptions.MaxColumnsIndexed,
                boundedOptions.TopValuesLimitPerColumn,
                boundedOptions.SampleRows,
                cancellationToken);

            foreach (var column in columns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (column.InferredType)
                {
                    case InferredType.Number:
                        column.NumericStats = await _metadataAnalyzer.ComputeNumericStatsAsync(
                            dataSet.StoredPath,
                            column.Name,
                            boundedOptions.SampleRows,
                            boundedOptions.IncludeDistributions,
                            boundedOptions.HistogramBins,
                            cancellationToken);
                        break;
                    case InferredType.Date:
                        column.DateStats = await _metadataAnalyzer.ComputeDateStatsAsync(
                            dataSet.StoredPath,
                            column.Name,
                            boundedOptions.SampleRows,
                            cancellationToken);
                        break;
                    case InferredType.String:
                    case InferredType.Category:
                        column.StringStats = await _metadataAnalyzer.ComputeStringStatsAsync(
                            dataSet.StoredPath,
                            column.Name,
                            boundedOptions.IncludeStringPatterns,
                            boundedOptions.SampleRows,
                            cancellationToken);
                        break;
                }
            }

            var candidateKeys = await _metadataAnalyzer.ComputeCandidateKeysAsync(
                dataSet.StoredPath,
                columns,
                boundedOptions.SampleRows,
                maxSingleColumnCandidates: 10,
                maxCompositeCandidates: 10,
                cancellationToken: cancellationToken);

            var correlations = await _metadataAnalyzer.ComputeNumericCorrelationsAsync(
                dataSet.StoredPath,
                columns,
                boundedOptions.MaxColumnsForCorrelation,
                boundedOptions.TopKEdgesPerColumn,
                boundedOptions.SampleRows,
                cancellationToken);

            var taggingResult = _semanticTagger.Tag(columns);
            var quality = BuildQuality(columns, profile.RowCount, candidateKeys);
            var globalStats = BuildGlobalStats(columns);

            var datasetIndex = new DatasetIndex
            {
                DatasetId = datasetId,
                BuiltAtUtc = DateTime.UtcNow,
                Version = IndexVersion,
                RowCount = profile.RowCount,
                ColumnCount = columns.Count,
                Quality = quality,
                Columns = columns,
                CandidateKeys = candidateKeys,
                Correlations = correlations,
                Tags = taggingResult.DatasetTags,
                Stats = globalStats,
                Limits = new IndexLimits
                {
                    MaxColumnsIndexed = boundedOptions.MaxColumnsIndexed,
                    MaxColumnsForCorrelation = boundedOptions.MaxColumnsForCorrelation,
                    TopKEdgesPerColumn = boundedOptions.TopKEdgesPerColumn,
                    SampleRows = boundedOptions.SampleRows,
                    IncludeStringPatterns = boundedOptions.IncludeStringPatterns,
                    IncludeDistributions = boundedOptions.IncludeDistributions
                }
            };

            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
            await _indexStore.SaveStatusAsync(new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = IndexBuildState.Ready,
                BuiltAtUtc = datasetIndex.BuiltAtUtc,
                Message = "Metadata index built successfully.",
                Version = IndexVersion
            }, cancellationToken);

            return datasetIndex;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build metadata index for dataset {DatasetId}", datasetId);

            await _indexStore.SaveStatusAsync(new DatasetIndexStatus
            {
                DatasetId = datasetId,
                Status = IndexBuildState.Failed,
                Message = ex.Message,
                Version = IndexVersion
            }, cancellationToken);

            throw;
        }
    }

    private static IndexBuildOptions NormalizeOptions(IndexBuildOptions options)
    {
        return new IndexBuildOptions
        {
            MaxColumnsIndexed = Math.Clamp(options.MaxColumnsIndexed, 1, 200),
            MaxColumnsForCorrelation = Math.Clamp(options.MaxColumnsForCorrelation, 2, 50),
            TopKEdgesPerColumn = Math.Clamp(options.TopKEdgesPerColumn, 1, 20),
            SampleRows = Math.Clamp(options.SampleRows, 1000, 100000),
            IncludeStringPatterns = options.IncludeStringPatterns,
            IncludeDistributions = options.IncludeDistributions,
            TopValuesLimitPerColumn = Math.Clamp(options.TopValuesLimitPerColumn, 3, 50),
            HistogramBins = Math.Clamp(options.HistogramBins, 4, 40)
        };
    }

    private static DatasetQualityIndex BuildQuality(
        IReadOnlyCollection<ColumnIndex> columns,
        int rowCount,
        IReadOnlyCollection<KeyCandidate> candidateKeys)
    {
        var nullRates = columns.Select(column => column.NullRate).ToList();
        var averageNullRate = nullRates.Count == 0 ? 0 : nullRates.Average();
        var medianNullRate = nullRates.Count == 0
            ? 0
            : nullRates.OrderBy(rate => rate).ElementAt(nullRates.Count / 2);

        var totalMissingValues = rowCount <= 0
            ? 0
            : (long)Math.Round(columns.Sum(column => column.NullRate * rowCount));

        var bestKey = candidateKeys
            .OrderByDescending(candidate => candidate.UniquenessRatio)
            .ThenBy(candidate => candidate.NullRate)
            .FirstOrDefault();

        var duplicateRowRate = bestKey == null
            ? 0
            : Math.Max(0, 1 - bestKey.UniquenessRatio);

        var warnings = new List<string>();
        if (columns.Count == 0)
        {
            warnings.Add("No columns were indexed.");
        }

        if (averageNullRate > 0.2)
        {
            warnings.Add("High missingness detected across dataset columns.");
        }

        if (bestKey == null || bestKey.UniquenessRatio < 0.9)
        {
            warnings.Add("No strong key candidate found.");
        }

        return new DatasetQualityIndex
        {
            DuplicateRowRate = duplicateRowRate,
            MissingnessSummary = new MissingnessSummaryIndex
            {
                TotalMissingValues = totalMissingValues,
                AverageNullRate = averageNullRate,
                MedianNullRate = medianNullRate,
                ColumnsWithNulls = columns.Count(column => column.NullRate > 0)
            },
            ParseIssuesCount = 0,
            Warnings = warnings
        };
    }

    private static GlobalStatsIndex BuildGlobalStats(IReadOnlyCollection<ColumnIndex> columns)
    {
        return new GlobalStatsIndex
        {
            NumericColumnCount = columns.LongCount(column => column.InferredType == InferredType.Number),
            DateColumnCount = columns.LongCount(column => column.InferredType == InferredType.Date),
            CategoryColumnCount = columns.LongCount(column => column.InferredType == InferredType.Category),
            StringColumnCount = columns.LongCount(column => column.InferredType == InferredType.String),
            BooleanColumnCount = columns.LongCount(column => column.InferredType == InferredType.Boolean)
        };
    }
}
