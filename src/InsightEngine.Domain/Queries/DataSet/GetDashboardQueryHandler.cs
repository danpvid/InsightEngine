using System.Text.Json;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Dashboard;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Services;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, Result<DashboardViewModel>>
{
    private const int MaxDashboardCharts = 9;
    private const int MaxSecondaryCharts = 8;
    private const string DashboardCacheVersion = "dashboard-v2";

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDataSetRepository _dataSetRepository;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IIndexStore _indexStore;
    private readonly IDashboardCacheRepository _dashboardCacheRepository;
    private readonly ICurrentUser _currentUser;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly IRecommendationEngineV2 _recommendationEngineV2;
    private readonly IAIInsightService _aiInsightService;
    private readonly InsightEngineFeatures _features;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GetDashboardQueryHandler> _logger;

    public GetDashboardQueryHandler(
        IDataSetRepository dataSetRepository,
        IDataSetSchemaStore schemaStore,
        IIndexStore indexStore,
        IDashboardCacheRepository dashboardCacheRepository,
        ICurrentUser currentUser,
        RecommendationEngine recommendationEngine,
        IRecommendationEngineV2 recommendationEngineV2,
        IAIInsightService aiInsightService,
        InsightEngineFeatures features,
        IUnitOfWork unitOfWork,
        ILogger<GetDashboardQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _schemaStore = schemaStore;
        _indexStore = indexStore;
        _dashboardCacheRepository = dashboardCacheRepository;
        _currentUser = currentUser;
        _recommendationEngine = recommendationEngine;
        _recommendationEngineV2 = recommendationEngineV2;
        _aiInsightService = aiInsightService;
        _features = features;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DashboardViewModel>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<DashboardViewModel>("Unauthorized");
        }

        var ownerUserId = _currentUser.UserId.Value;
        var dataset = await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, ownerUserId, cancellationToken);
        if (dataset is null)
        {
            return Result.Failure<DashboardViewModel>("Dataset not found.");
        }

        var schema = await _schemaStore.LoadAsync(request.DatasetId, cancellationToken);
        var index = await _indexStore.LoadAsync(request.DatasetId, cancellationToken);
        var sourceDatasetUpdatedAt = dataset.UpdatedAt ?? dataset.CreatedAt;
        var sourceFingerprint = BuildSourceFingerprint(index);

        var cached = await TryReadCachedDashboardAsync(
            ownerUserId,
            request.DatasetId,
            sourceDatasetUpdatedAt,
            sourceFingerprint,
            cancellationToken);
        if (cached is not null)
        {
            return Result.Success(cached);
        }

        var dashboard = new DashboardViewModel
        {
            Dataset = BuildDatasetSummary(dataset, schema, index),
            Metadata = BuildMetadata(index),
            RenderingHints = new DashboardRenderingHints
            {
                NumberFormat = new DashboardNumberFormatHints
                {
                    Mode = "compact",
                    Locale = "pt-BR"
                },
                MultiSeriesPolicy = new DashboardMultiSeriesPolicy
                {
                    MaxSeries = 3,
                    MaxLegendItems = 5
                }
            },
            LastUpdated = sourceDatasetUpdatedAt,
            Generation = new DashboardGenerationTimestamps
            {
                IndexGeneratedAt = index?.BuiltAtUtc
            }
        };

        dashboard.Kpis = BuildKpis(dashboard.Dataset, index);
        dashboard.Tables = BuildTables(index);

        if (index is not null)
        {
            var profile = BuildProfile(index, schema);
            var recommendations = _features.RecommendationV2Enabled
                ? _recommendationEngineV2.Generate(profile, index)
                : _recommendationEngine.Generate(profile);

            dashboard.Charts = recommendations.Take(MaxDashboardCharts).ToList();
            dashboard.HeroChart = dashboard.Charts.FirstOrDefault();
            dashboard.SecondaryCharts = dashboard.Charts.Skip(1).Take(MaxSecondaryCharts).ToList();
            dashboard.Metadata.RecommendationsAvailable = dashboard.Charts.Count > 0;
            dashboard.Generation.RecommendationsGeneratedAt = dashboard.Charts.Count > 0 ? DateTime.UtcNow : null;

            dashboard.Insights = await BuildInsightsAsync(
                request.DatasetId,
                dashboard.HeroChart,
                index,
                dashboard.Tables.TopFeatures,
                cancellationToken);
            dashboard.Generation.InsightsGeneratedAt = DateTime.UtcNow;
        }
        else
        {
            dashboard.Insights = new DashboardInsights
            {
                Warnings =
                {
                    "Index metadata not available. Generate index to unlock full dashboard insights."
                },
                NextActions =
                {
                    "Reimporte o dataset no modo WithIndex.",
                    "Defina uma coluna alvo para recomendações orientadas.",
                    "Atualize o dashboard após gerar o índice."
                }
            };
        }

        await SaveDashboardCacheAsync(
            ownerUserId,
            request.DatasetId,
            sourceDatasetUpdatedAt,
            sourceFingerprint,
            dashboard,
            cancellationToken);

        return Result.Success(dashboard);
    }

    private async Task<DashboardViewModel?> TryReadCachedDashboardAsync(
        Guid ownerUserId,
        Guid datasetId,
        DateTime sourceDatasetUpdatedAt,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = await _dashboardCacheRepository.GetAsync(ownerUserId, datasetId, DashboardCacheVersion, cancellationToken);
            if (entry is null)
            {
                return null;
            }

            if (sourceDatasetUpdatedAt > entry.SourceDatasetUpdatedAt)
            {
                return null;
            }

            if (!string.Equals(entry.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<DashboardViewModel>(entry.PayloadJson, CacheJsonOptions);
            if (payload is null)
            {
                return null;
            }

            _logger.LogInformation("Dashboard cache hit. DatasetId={DatasetId}", datasetId);
            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard cache read failed. DatasetId={DatasetId}", datasetId);
            return null;
        }
    }

    private async Task SaveDashboardCacheAsync(
        Guid ownerUserId,
        Guid datasetId,
        DateTime sourceDatasetUpdatedAt,
        string sourceFingerprint,
        DashboardViewModel dashboard,
        CancellationToken cancellationToken)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(dashboard, CacheJsonOptions);
            var existing = await _dashboardCacheRepository.GetAsync(ownerUserId, datasetId, DashboardCacheVersion, cancellationToken);
            if (existing is null)
            {
                var entry = new DashboardCacheEntry(
                    ownerUserId,
                    datasetId,
                    DashboardCacheVersion,
                    payloadJson,
                    sourceDatasetUpdatedAt,
                    sourceFingerprint);
                await _dashboardCacheRepository.AddAsync(entry);
            }
            else
            {
                existing.UpdatePayload(payloadJson, sourceDatasetUpdatedAt, sourceFingerprint);
                _dashboardCacheRepository.Update(existing);
            }

            await _unitOfWork.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dashboard cache save failed. DatasetId={DatasetId}", datasetId);
        }
    }

    private async Task<DashboardInsights> BuildInsightsAsync(
        Guid datasetId,
        ChartRecommendation? heroChart,
        DatasetIndex index,
        IReadOnlyList<DashboardTopFeatureRow> topFeatures,
        CancellationToken cancellationToken)
    {
        var insights = new DashboardInsights();

        if (heroChart is not null)
        {
            var insightResult = await _aiInsightService.GenerateAiSummaryAsync(new LLMChartContextRequest
            {
                DatasetId = datasetId,
                RecommendationId = heroChart.Id,
                Language = "pt-br"
            }, cancellationToken);

                if (insightResult.IsSuccess && insightResult.Data is not null)
                {
                    insights.LlmExecutiveSummary = insightResult.Data.InsightSummary.Headline;
                    insights.ExecutiveBullets = insightResult.Data.InsightSummary.BulletPoints.Take(5).ToList();
                    insights.Warnings = insightResult.Data.InsightSummary.Cautions.Take(5).ToList();
                }
            }

        if (insights.Warnings.Count == 0)
        {
            insights.Warnings = CollectWarnings(index);
        }

        insights.KeyDrivers = topFeatures
            .Take(5)
            .Select(item => item.Correlation.HasValue
                ? $"{item.Column} (corr: {item.Correlation.Value:0.###})"
                : $"{item.Column} (score: {item.Score:0.###})")
            .ToList();
        insights.NextActions = BuildNextActions(index);
        return insights;
    }

    private static DashboardDatasetSummary BuildDatasetSummary(
        global::InsightEngine.Domain.Entities.DataSet dataset,
        DatasetImportSchema? schema,
        DatasetIndex? index)
    {
        var rowCount = index?.RowCount ?? dataset.RowCount ?? 0;
        var columnCount = index?.ColumnCount ?? schema?.Columns.Count ?? 0;
        var targetColumn = index?.TargetColumn ?? schema?.TargetColumn;

        return new DashboardDatasetSummary
        {
            Id = dataset.Id,
            Name = dataset.OriginalFileName,
            RowCount = rowCount,
            ColumnCount = columnCount,
            CreatedAt = dataset.CreatedAt,
            UpdatedAt = dataset.UpdatedAt,
            TargetColumn = targetColumn
        };
    }

    private static DashboardMetadata BuildMetadata(DatasetIndex? index)
    {
        var metadata = new DashboardMetadata
        {
            IndexAvailable = index is not null,
            RecommendationsAvailable = false,
            FormulaAvailable = index?.SelectedFormula is not null || index?.FormulaInference?.Result?.Candidates.Count > 0
        };

        var selectedFormula = index?.SelectedFormula;
        var bestCandidate = index?.FormulaInference?.Result?.Candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .FirstOrDefault();

        if (selectedFormula is not null)
        {
            metadata.FormulaSummary = new DashboardFormulaSummary
            {
                Expression = selectedFormula.Formula.ExpressionText,
                Error = selectedFormula.Formula.EpsilonMaxAbsError,
                Confidence = selectedFormula.Formula.Confidence.ToString()
            };
        }
        else if (bestCandidate is not null)
        {
            metadata.FormulaSummary = new DashboardFormulaSummary
            {
                Expression = bestCandidate.ExpressionText,
                Error = bestCandidate.EpsilonMaxAbsError,
                Confidence = bestCandidate.Confidence.ToString()
            };
        }

        return metadata;
    }

    private static List<DashboardKpiCard> BuildKpis(DashboardDatasetSummary? dataset, DatasetIndex? index)
    {
        var kpis = new List<DashboardKpiCard>();
        if (dataset is null)
        {
            return kpis;
        }

        kpis.Add(new DashboardKpiCard { Key = "rows", Label = "Rows", Value = dataset.RowCount.ToString("N0") });
        kpis.Add(new DashboardKpiCard { Key = "columns", Label = "Columns", Value = dataset.ColumnCount.ToString("N0") });

        if (index is null)
        {
            return kpis;
        }

        kpis.Add(new DashboardKpiCard
        {
            Key = "nullRateAvg",
            Label = "Avg Null Rate",
            Value = $"{(index.Quality.MissingnessSummary.AverageNullRate * 100):0.##}%"
        });
        kpis.Add(new DashboardKpiCard
        {
            Key = "columnsWithNulls",
            Label = "Columns With Nulls",
            Value = index.Quality.MissingnessSummary.ColumnsWithNulls.ToString("N0")
        });

        var outlierColumnsCount = index.Columns.Count(column => EstimateOutlierRate(column) >= 0.1d);
        kpis.Add(new DashboardKpiCard
        {
            Key = "outlierColumns",
            Label = "Outlier Columns",
            Value = outlierColumnsCount.ToString("N0")
        });
        kpis.Add(new DashboardKpiCard
        {
            Key = "duplicateRate",
            Label = "Duplicate Rows",
            Value = $"{(index.Quality.DuplicateRowRate * 100):0.##}%"
        });

        if (!string.IsNullOrWhiteSpace(dataset.TargetColumn))
        {
            kpis.Add(new DashboardKpiCard
            {
                Key = "targetColumn",
                Label = "Target",
                Value = dataset.TargetColumn
            });
        }

        var topCorrelation = ResolveTopCorrelation(index);
        if (topCorrelation is not null)
        {
            kpis.Add(new DashboardKpiCard
            {
                Key = "topCorrelation",
                Label = "Top Correlation",
                Value = $"{topCorrelation.Value.Column}: {topCorrelation.Value.Score:0.###}"
            });
        }

        var dateRange = ResolveDateRange(index);
        if (!string.IsNullOrWhiteSpace(dateRange))
        {
            kpis.Add(new DashboardKpiCard
            {
                Key = "dateRange",
                Label = "Date Range",
                Value = dateRange
            });
        }

        return kpis;
    }

    private static DashboardTables BuildTables(DatasetIndex? index)
    {
        var tables = new DashboardTables();
        if (index is null)
        {
            return tables;
        }

        tables.TopFeatures = BuildTopFeatureRows(index);
        tables.DataQuality = index.Columns
            .OrderByDescending(column => column.NullRate)
            .ThenByDescending(column => EstimateOutlierRate(column))
            .Take(8)
            .Select(column => new DashboardDataQualityRow
            {
                Column = column.Name,
                NullRate = Math.Round(column.NullRate, 6),
                OutlierRate = Math.Round(EstimateOutlierRate(column), 6),
                DistinctCount = column.DistinctCount
            })
            .ToList();

        tables.TopCategories = index.Columns
            .Where(column => column.NumericStats is null && column.TopValues.Count > 0)
            .OrderByDescending(column => column.DistinctCount)
            .Take(4)
            .SelectMany(column => column.TopValues.Take(3).Select(value => new DashboardCategorySummaryRow
            {
                Column = column.Name,
                Category = value,
                Count = 0
            }))
            .ToList();

        return tables;
    }

    private static List<DashboardTopFeatureRow> BuildTopFeatureRows(DatasetIndex index)
    {
        if (!string.IsNullOrWhiteSpace(index.TargetColumn))
        {
            return index.Correlations.Edges
                .Where(edge => string.Equals(edge.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(edge.RightColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase))
                .Select(edge =>
                {
                    var columnName = string.Equals(edge.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase)
                        ? edge.RightColumn
                        : edge.LeftColumn;
                    var column = index.Columns.FirstOrDefault(item => string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
                    return new DashboardTopFeatureRow
                    {
                        Column = columnName,
                        Score = Math.Abs(edge.Score),
                        Correlation = Math.Round(edge.Score, 6),
                        VarianceNorm = column is null ? null : Math.Round(ResolveVarianceNorm(column), 6),
                        NullRate = column?.NullRate ?? 0d,
                        CardinalityRatio = ResolveCardinalityRatio(column, index.RowCount)
                    };
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Column, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        return index.Columns
            .Where(column => column.NumericStats is not null)
            .Select(column => new DashboardTopFeatureRow
            {
                Column = column.Name,
                Score = ResolveVarianceNorm(column),
                Correlation = null,
                VarianceNorm = Math.Round(ResolveVarianceNorm(column), 6),
                NullRate = Math.Round(column.NullRate, 6),
                CardinalityRatio = ResolveCardinalityRatio(column, index.RowCount)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Column, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static DatasetProfile BuildProfile(DatasetIndex index, DatasetImportSchema? schema)
    {
        var ignoredColumns = schema?.Columns.Where(column => column.IsIgnored).Select(column => column.Name).ToList() ?? new List<string>();

        return new DatasetProfile
        {
            DatasetId = index.DatasetId,
            RowCount = (int)Math.Min(int.MaxValue, Math.Max(index.RowCount, 0)),
            SampleSize = (int)Math.Min(int.MaxValue, Math.Max(index.RowCount, 0)),
            TargetColumn = index.TargetColumn ?? schema?.TargetColumn,
            IgnoredColumns = ignoredColumns,
            SchemaConfirmed = index.SchemaConfirmed,
            Columns = index.Columns.Select(column => new ColumnProfile
            {
                Name = column.Name,
                InferredType = column.InferredType,
                ConfirmedType = column.InferredType,
                IsIgnored = ignoredColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase),
                IsTarget = string.Equals(column.Name, index.TargetColumn, StringComparison.OrdinalIgnoreCase),
                NullRate = column.NullRate,
                DistinctCount = (int)Math.Min(int.MaxValue, Math.Max(column.DistinctCount, 0)),
                TopValues = column.TopValues.ToList(),
                Min = column.NumericStats?.Min,
                Mean = column.NumericStats?.Mean,
                Max = column.NumericStats?.Max
            }).ToList()
        };
    }

    private string BuildSourceFingerprint(DatasetIndex? index)
    {
        if (index is null)
        {
            return $"idx:none|recV2:{_features.RecommendationV2Enabled}|llmV2:{_features.LlmStructuredInsightsV2Enabled}";
        }

        var selectedFormulaAt = index.SelectedFormula?.SelectedAtUtc.ToUnixTimeSeconds() ?? 0;
        var formulaInferenceAt = index.FormulaInference?.UpdatedAtUtc.ToUnixTimeSeconds() ?? 0;
        var formulaDiscoveryAt = index.FormulaDiscovery?.UpdatedAtUtc.ToUnixTimeSeconds() ?? 0;
        return $"idx:{index.BuiltAtUtc:O}|ver:{index.Version}|target:{index.TargetColumn}|sel:{selectedFormulaAt}|inf:{formulaInferenceAt}|disc:{formulaDiscoveryAt}|recV2:{_features.RecommendationV2Enabled}|llmV2:{_features.LlmStructuredInsightsV2Enabled}";
    }

    private static (string Column, double Score)? ResolveTopCorrelation(DatasetIndex index)
    {
        if (string.IsNullOrWhiteSpace(index.TargetColumn))
        {
            return null;
        }

        var top = index.Correlations.Edges
            .Where(edge => string.Equals(edge.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(edge.RightColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(edge => Math.Abs(edge.Score))
            .FirstOrDefault();

        if (top is null)
        {
            return null;
        }

        var columnName = string.Equals(top.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase)
            ? top.RightColumn
            : top.LeftColumn;
        return (columnName, top.Score);
    }

    private static string ResolveDateRange(DatasetIndex index)
    {
        var minDate = index.Columns
            .Where(column => column.DateStats?.Min is not null)
            .Select(column => column.DateStats!.Min!.Value)
            .DefaultIfEmpty()
            .Min();
        var maxDate = index.Columns
            .Where(column => column.DateStats?.Max is not null)
            .Select(column => column.DateStats!.Max!.Value)
            .DefaultIfEmpty()
            .Max();

        if (minDate == default || maxDate == default)
        {
            return string.Empty;
        }

        return $"{minDate:yyyy-MM-dd} .. {maxDate:yyyy-MM-dd}";
    }

    private static double ResolveVarianceNorm(ColumnIndex column)
    {
        var stats = column.NumericStats;
        if (stats?.P50 is null || stats.P90 is null || stats.P50.Value == 0)
        {
            return 0;
        }

        var spread = Math.Abs(stats.P90.Value - stats.P50.Value);
        var basis = Math.Abs(stats.P50.Value);
        return Math.Clamp(spread / basis, 0d, 1d);
    }

    private static double ResolveCardinalityRatio(ColumnIndex? column, long rowCount)
    {
        if (column is null || rowCount <= 0)
        {
            return 0;
        }

        return Math.Round(Math.Clamp((double)column.DistinctCount / rowCount, 0d, 1d), 6);
    }

    private static double EstimateOutlierRate(ColumnIndex column)
    {
        var stats = column.NumericStats;
        if (stats?.P5 is null || stats.P95 is null || stats.Min is null || stats.Max is null)
        {
            return 0;
        }

        var spread = Math.Abs(stats.P95.Value - stats.P5.Value);
        if (spread <= 0)
        {
            return 0;
        }

        var tailDistance = Math.Abs(stats.Min.Value - stats.P5.Value) + Math.Abs(stats.Max.Value - stats.P95.Value);
        return Math.Clamp(tailDistance / spread, 0d, 1d);
    }

    private static List<string> CollectWarnings(DatasetIndex index)
    {
        var warnings = new List<string>();
        if (index.Quality.Warnings.Count > 0)
        {
            warnings.AddRange(index.Quality.Warnings.Take(5));
        }

        if (warnings.Count == 0 && index.Quality.MissingnessSummary.AverageNullRate > 0.1)
        {
            warnings.Add("Dataset has relevant null-rate concentration in key columns.");
        }

        return warnings;
    }

    private static List<string> BuildNextActions(DatasetIndex index)
    {
        var actions = new List<string>();
        if (string.IsNullOrWhiteSpace(index.TargetColumn))
        {
            actions.Add("Defina uma coluna alvo para análises orientadas por correlação.");
        }
        else
        {
            actions.Add($"Valide os drivers principais da coluna alvo \"{index.TargetColumn}\".");
        }

        if (index.Quality.MissingnessSummary.AverageNullRate > 0.05)
        {
            actions.Add("Priorize tratamento de colunas com null rate elevado.");
        }

        actions.Add("Abra os gráficos recomendados para detalhar segmentos críticos.");
        return actions.Take(3).ToList();
    }

}
