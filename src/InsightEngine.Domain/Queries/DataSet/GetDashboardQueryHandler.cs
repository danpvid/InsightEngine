using InsightEngine.Domain.Core;
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
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IDataSetSchemaStore _schemaStore;
    private readonly IIndexStore _indexStore;
    private readonly ICurrentUser _currentUser;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly IRecommendationEngineV2 _recommendationEngineV2;
    private readonly IAIInsightService _aiInsightService;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<GetDashboardQueryHandler> _logger;

    public GetDashboardQueryHandler(
        IDataSetRepository dataSetRepository,
        IDataSetSchemaStore schemaStore,
        IIndexStore indexStore,
        ICurrentUser currentUser,
        RecommendationEngine recommendationEngine,
        IRecommendationEngineV2 recommendationEngineV2,
        IAIInsightService aiInsightService,
        InsightEngineFeatures features,
        ILogger<GetDashboardQueryHandler> logger)
    {
        _dataSetRepository = dataSetRepository;
        _schemaStore = schemaStore;
        _indexStore = indexStore;
        _currentUser = currentUser;
        _recommendationEngine = recommendationEngine;
        _recommendationEngineV2 = recommendationEngineV2;
        _aiInsightService = aiInsightService;
        _features = features;
        _logger = logger;
    }

    public async Task<Result<DashboardViewModel>> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<DashboardViewModel>("Unauthorized");
        }

        _logger.LogInformation("Composing dashboard for dataset {DatasetId}", request.DatasetId);

        var dataset = await _dataSetRepository.GetByIdForOwnerAsync(request.DatasetId, _currentUser.UserId.Value, cancellationToken);
        if (dataset is null)
        {
            return Result.Failure<DashboardViewModel>("Dataset not found.");
        }

        var schema = await _schemaStore.LoadAsync(request.DatasetId, cancellationToken);
        var index = await _indexStore.LoadAsync(request.DatasetId, cancellationToken);

        var dashboard = new DashboardViewModel
        {
            Dataset = BuildDatasetSummary(dataset, schema, index),
            Metadata = BuildMetadata(index),
            LastUpdated = dataset.UpdatedAt ?? dataset.CreatedAt,
            Generation = new DashboardGenerationTimestamps
            {
                IndexGeneratedAt = index?.BuiltAtUtc
            }
        };

        dashboard.Kpis = BuildKpis(dashboard.Dataset, index);
        dashboard.Tables = BuildTables(index, schema);

        if (index is not null)
        {
            var profile = BuildProfile(index, schema);
            var recommendations = _features.RecommendationV2Enabled
                ? _recommendationEngineV2.Generate(profile, index)
                : _recommendationEngine.Generate(profile);

            dashboard.Charts = recommendations.Take(MaxDashboardCharts).ToList();
            dashboard.Metadata.RecommendationsAvailable = dashboard.Charts.Count > 0;
            dashboard.Generation.RecommendationsGeneratedAt = dashboard.Charts.Count > 0 ? DateTime.UtcNow : null;

            if (dashboard.Charts.Count > 0)
            {
                var insightResult = await _aiInsightService.GenerateAiSummaryAsync(new LLMChartContextRequest
                {
                    DatasetId = request.DatasetId,
                    RecommendationId = dashboard.Charts[0].Id,
                    Language = "pt-br"
                }, cancellationToken);

                if (insightResult.IsSuccess && insightResult.Data is not null)
                {
                    dashboard.Insights.LlmExecutiveSummary = insightResult.Data.InsightSummary.Headline;
                    dashboard.Insights.Warnings = insightResult.Data.InsightSummary.Cautions;
                    dashboard.Generation.InsightsGeneratedAt = DateTime.UtcNow;
                }
                else
                {
                    dashboard.Insights.Warnings = CollectWarnings(index);
                }
            }
            else
            {
                dashboard.Insights.Warnings = CollectWarnings(index);
            }
        }
        else
        {
            _logger.LogInformation("Dataset {DatasetId} has no index metadata; returning dashboard fallback", request.DatasetId);
            dashboard.Insights.Warnings = new List<string>
            {
                "Index metadata not available. Generate index to unlock full dashboard insights."
            };
        }

        return Result.Success(dashboard);
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

        var outlierColumnsCount = index.Columns.Count(column => EstimateOutlierRate(column) >= 0.1d);
        kpis.Add(new DashboardKpiCard
        {
            Key = "outlierColumns",
            Label = "Outlier Columns",
            Value = outlierColumnsCount.ToString("N0")
        });

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

        return kpis;
    }

    private static DashboardTables BuildTables(DatasetIndex? index, DatasetImportSchema? schema)
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
}
