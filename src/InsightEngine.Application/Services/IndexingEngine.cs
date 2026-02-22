using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.Formulas;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Application.Services;

public class IndexingEngine : IIndexingEngine
{
    private const string IndexVersion = "metadata-index/v1";

    private readonly IDataSetRepository _dataSetRepository;
    private readonly ICsvProfiler _csvProfiler;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IDuckDbMetadataAnalyzer _metadataAnalyzer;
    private readonly ISemanticTagger _semanticTagger;
    private readonly IIndexStore _indexStore;
    private readonly IFormulaInferenceEngine _formulaInferenceEngine;
    private readonly ILogger<IndexingEngine> _logger;

    public IndexingEngine(
        IDataSetRepository dataSetRepository,
        ICsvProfiler csvProfiler,
        IDataSetSchemaStore schemaStore,
        IDuckDbMetadataAnalyzer metadataAnalyzer,
        ISemanticTagger semanticTagger,
        IIndexStore indexStore,
        IFormulaInferenceEngine formulaInferenceEngine,
        ILogger<IndexingEngine> logger)
    {
        _dataSetRepository = dataSetRepository;
        _csvProfiler = csvProfiler;
        _schemaStore = schemaStore;
        _metadataAnalyzer = metadataAnalyzer;
        _semanticTagger = semanticTagger;
        _indexStore = indexStore;
        _formulaInferenceEngine = formulaInferenceEngine;
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
            var schema = await _schemaStore.LoadAsync(datasetId, cancellationToken);
            profile = DatasetSchemaProfileMapper.ApplySchema(profile, schema);

            var columns = await _metadataAnalyzer.ComputeColumnProfilesAsync(
                dataSet.StoredPath,
                boundedOptions.MaxColumnsIndexed,
                boundedOptions.TopValuesLimitPerColumn,
                boundedOptions.SampleRows,
                cancellationToken);

            if (profile.Columns.Count > 0)
            {
                var profileByName = profile.Columns.ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
                columns = columns
                    .Where(column => profileByName.ContainsKey(column.Name))
                    .Select(column =>
                    {
                        var mapped = profileByName[column.Name];
                        column.InferredType = (mapped.ConfirmedType ?? mapped.InferredType).NormalizeLegacy();
                        return column;
                    })
                    .ToList();
            }

            foreach (var column in columns)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (column.InferredType)
                {
                    case InferredType.Integer:
                    case InferredType.Decimal:
                    case InferredType.Percentage:
                    case InferredType.Money:
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
                SchemaConfirmed = schema?.SchemaConfirmed ?? false,
                TargetColumn = schema?.TargetColumn,
                IgnoredColumnsCount = profile.IgnoredColumns.Count,
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
            await TryRunFormulaInferenceForIndexBuildAsync(datasetId, datasetIndex, cancellationToken);
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

    private async Task TryRunFormulaInferenceForIndexBuildAsync(
        Guid datasetId,
        DatasetIndex datasetIndex,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datasetIndex.TargetColumn))
        {
            datasetIndex.FormulaInference = BuildSkippedInferenceResult(
                datasetIndex.TargetColumn,
                "Target column is not configured for this dataset.");
            datasetIndex.TargetFormulaSuggestion = null;
            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
            return;
        }

        var targetColumn = datasetIndex.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, datasetIndex.TargetColumn, StringComparison.OrdinalIgnoreCase));

        if (targetColumn is null)
        {
            datasetIndex.FormulaInference = BuildSkippedInferenceResult(
                datasetIndex.TargetColumn,
                "Target column was not found in indexed columns.");
            datasetIndex.TargetFormulaSuggestion = null;
            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
            return;
        }

        if (!targetColumn.InferredType.IsNumericLike())
        {
            datasetIndex.FormulaInference = BuildSkippedInferenceResult(
                targetColumn.Name,
                "Target column must be numeric for formula inference.");
            datasetIndex.TargetFormulaSuggestion = null;
            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
            return;
        }

        var numericCandidates = datasetIndex.Columns
            .Where(column =>
                !string.Equals(column.Name, targetColumn.Name, StringComparison.OrdinalIgnoreCase)
                && column.InferredType.IsNumericLike())
            .Select(column => column.Name)
            .ToList();

        if (numericCandidates.Count < 2)
        {
            datasetIndex.FormulaInference = BuildSkippedInferenceResult(
                targetColumn.Name,
                "At least 2 numeric candidate columns are required.");
            datasetIndex.TargetFormulaSuggestion = null;
            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
            return;
        }

        try
        {
            var inferenceResult = await _formulaInferenceEngine.InferAsync(
                datasetId,
                targetColumn.Name,
                numericCandidates,
                new FormulaInferenceSettings
                {
                    EnabledDefault = true,
                    MaxColumns = 10,
                    MaxDepth = 5,
                    MaxCandidatesReturned = 5,
                    SearchBudgetMs = 2500,
                    InitialSampleRows = 300,
                    ValidationSampleRows = 2000,
                    EpsilonAbs = 1e-6,
                    EpsilonAbsRelaxed = 1e-3,
                    DivisionZeroEpsilon = 1e-12,
                    AllowConstants = false,
                    AllowColumnReuse = false,
                    BeamWidth = 200
                },
                cancellationToken);

            datasetIndex.FormulaInference = new FormulaInferenceIndexEntry
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Result = inferenceResult
            };
            datasetIndex.TargetFormulaSuggestion = BuildTargetFormulaSuggestion(inferenceResult);

            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Index-time formula inference failed for dataset {DatasetId}", datasetId);
            datasetIndex.FormulaInference = BuildSkippedInferenceResult(
                targetColumn.Name,
                "Formula inference failed during index build.");
            datasetIndex.TargetFormulaSuggestion = null;
            await _indexStore.SaveAsync(datasetIndex, cancellationToken);
        }
    }

    private static FormulaInferenceIndexEntry BuildSkippedInferenceResult(string? targetColumn, string warning)
    {
        return new FormulaInferenceIndexEntry
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Result = new FormulaInferenceResult
            {
                Status = FormulaInferenceStatus.NotRun,
                GeneratedAt = DateTimeOffset.UtcNow,
                TargetColumn = targetColumn ?? string.Empty,
                Warnings = [warning],
                Meta = new Dictionary<string, object?>
                {
                    ["trigger"] = "index-build"
                }
            }
        };
    }

    private static TargetFormulaSuggestionIndexEntry? BuildTargetFormulaSuggestion(FormulaInferenceResult inferenceResult)
    {
        var bestCandidate = inferenceResult.Candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.UsedColumns.Length)
            .ThenBy(candidate => candidate.Depth)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        return new TargetFormulaSuggestionIndexEntry
        {
            BestCandidateExpressionText = bestCandidate.ExpressionText,
            Confidence = bestCandidate.Confidence,
            UsedColumns = bestCandidate.UsedColumns
        };
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
            NumericColumnCount = columns.LongCount(column => column.InferredType.IsNumericLike()),
            DateColumnCount = columns.LongCount(column => column.InferredType == InferredType.Date),
            CategoryColumnCount = columns.LongCount(column => column.InferredType == InferredType.Category),
            StringColumnCount = columns.LongCount(column => column.InferredType == InferredType.String),
            BooleanColumnCount = columns.LongCount(column => column.InferredType == InferredType.Boolean)
        };
    }
}
