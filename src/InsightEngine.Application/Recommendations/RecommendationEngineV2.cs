using InsightEngine.Application.Recommendations.Models;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Charts;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Recommendations.Scoring;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Application.Recommendations;

public class RecommendationEngineV2 : IRecommendationEngineV2
{
    private const int MaxRecommendations = 12;

    private readonly RecommendationWeights _weights;
    private readonly ChartRelevanceScorer _scorer;
    private readonly InsightEngineFeatures _features;
    private readonly ILogger<RecommendationEngineV2> _logger;

    public RecommendationEngineV2(
        IOptions<RecommendationWeights> weightOptions,
        IOptions<InsightEngineFeatures> featureOptions,
        ChartRelevanceScorer scorer,
        ILogger<RecommendationEngineV2> logger)
    {
        _weights = weightOptions.Value ?? new RecommendationWeights();
        _scorer = scorer;
        _logger = logger;
        _features = featureOptions.Value ?? new InsightEngineFeatures();
    }

    public List<ChartRecommendation> Generate(DatasetProfile profile, DatasetIndex? index)
    {
        var candidates = BuildCandidates(profile, index);

        var debug = new List<RecommendationDebugInfo>();
        foreach (var candidate in candidates)
        {
            var (score, components) = _scorer.Score(candidate, index, _weights);
            candidate.Score = score;
            candidate.ImpactScore = Math.Round((components.Correlation + components.Variance + components.SemanticHint) / 3d, 4);
            candidate.ScoreCriteria = BuildScoreCriteria(components);
            candidate.OptionTemplate = Domain.Services.EChartsOptionTemplateFactory.Create(candidate);

            if (_features.RecommendationV2DebugLogging)
            {
                debug.Add(new RecommendationDebugInfo
                {
                    RecommendationId = candidate.Id,
                    Title = candidate.Title,
                    Components = components
                });
            }
        }

        var final = candidates
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.ImpactScore)
            .Take(MaxRecommendations)
            .ToList();

        if (_features.RecommendationV2DebugLogging)
        {
            foreach (var item in debug.OrderByDescending(d => d.Components.FinalScore).Take(20))
            {
                _logger.LogInformation(
                    "[RecommendationV2] Candidate {Id} {Title} score={Score:F4} corr={Corr:F3} var={Var:F3} comp={Comp:F3} out={Out:F3} temp={Temp:F3} role={Role:F3} sem={Sem:F3} cardPenalty={Card:F3} constPenalty={Const:F3}",
                    item.RecommendationId,
                    item.Title,
                    item.Components.FinalScore,
                    item.Components.Correlation,
                    item.Components.Variance,
                    item.Components.Completeness,
                    item.Components.Outlier,
                    item.Components.Temporal,
                    item.Components.RoleHint,
                    item.Components.SemanticHint,
                    item.Components.CardinalityPenalty,
                    item.Components.NearConstantPenalty);
            }

            _logger.LogInformation(
                "[RecommendationV2] Final chosen recommendations: {Ids}",
                string.Join(", ", final.Select(item => item.Id)));
        }

        return final;
    }

    private static List<string> BuildScoreCriteria(ScoreComponents components)
    {
        var criteria = new List<string>
        {
            $"corr:{components.Correlation:F3}",
            $"var:{components.Variance:F3}",
            $"completeness:{components.Completeness:F3}",
            $"temporal:{components.Temporal:F3}",
            $"semantic:{components.SemanticHint:F3}"
        };

        if (components.CardinalityPenalty > 0)
        {
            criteria.Add($"cardinality-penalty:{components.CardinalityPenalty:F3}");
        }

        if (components.NearConstantPenalty > 0)
        {
            criteria.Add($"near-constant-penalty:{components.NearConstantPenalty:F3}");
        }

        return criteria;
    }

    private static List<ChartRecommendation> BuildCandidates(DatasetProfile profile, DatasetIndex? index)
    {
        var recommendations = new List<ChartRecommendation>();
        var activeColumns = profile.Columns.Where(column => !column.IsIgnored).ToList();
        if (activeColumns.Count == 0)
        {
            activeColumns = profile.Columns;
        }

        var numeric = activeColumns
            .Where(column => (column.ConfirmedType ?? column.InferredType) == InferredType.Number)
            .ToList();
        var categorical = activeColumns
            .Where(column =>
                (column.ConfirmedType ?? column.InferredType) is InferredType.Category or InferredType.String or InferredType.Boolean)
            .ToList();
        var dates = activeColumns
            .Where(column => (column.ConfirmedType ?? column.InferredType) == InferredType.Date)
            .ToList();

        var target = !string.IsNullOrWhiteSpace(profile.TargetColumn)
            ? activeColumns.FirstOrDefault(column =>
                string.Equals(column.Name, profile.TargetColumn, StringComparison.OrdinalIgnoreCase))
            : null;

        var counter = 1;

        foreach (var column in numeric.Take(4))
        {
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "DistributionHistogramV2",
                Title = $"Distribuição de {column.Name}",
                Reason = "Distribuição de coluna numérica com variabilidade relevante.",
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Histogram },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = column.Name, Role = AxisRole.Measure },
                    Y = new FieldSpec { Column = "count", Role = AxisRole.Measure, Aggregation = Aggregation.Count }
                },
                IncludedColumns = new RecommendationIncludedColumns { X = column.Name, Y = [column.Name] },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = "Count",
                    SupportedAggregations = ["Count"]
                }
            });
        }

        foreach (var column in categorical.Take(4))
        {
            var measure = target ?? numeric.FirstOrDefault();
            if (measure is null)
            {
                continue;
            }

            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "CategoryComparisonV2",
                Title = $"Comparação por {column.Name}",
                Reason = "Comparação por categoria com agregação do indicador principal.",
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Bar },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = column.Name, Role = AxisRole.Category },
                    Y = new FieldSpec
                    {
                        Column = measure.Name,
                        Role = AxisRole.Measure,
                        Aggregation = ResolveAggregation(measure.Name)
                    },
                    TopN = 10
                },
                IncludedColumns = new RecommendationIncludedColumns { X = column.Name, Y = [measure.Name] },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = ResolveAggregation(measure.Name).ToString(),
                    SupportedAggregations = ["Sum", "Avg", "Count"]
                }
            });
        }

        if (dates.Count > 0)
        {
            var time = dates[0];
            var measure = target ?? numeric.FirstOrDefault();
            if (measure is not null)
            {
                recommendations.Add(new ChartRecommendation
                {
                    Id = $"rec_{counter++:D3}",
                    TemplateType = "TrendTimeSeriesV2",
                    Title = $"Tendência de {measure.Name} ao longo do tempo",
                    Reason = "Série temporal para identificar tendência e sazonalidade.",
                    Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec { Column = time.Name, Role = AxisRole.Time, Bin = TimeBin.Month },
                        Y = new FieldSpec
                        {
                            Column = measure.Name,
                            Role = AxisRole.Measure,
                            Aggregation = ResolveAggregation(measure.Name)
                        }
                    },
                    IncludedColumns = new RecommendationIncludedColumns { X = time.Name, Y = [measure.Name] },
                    AggregationPlan = new RecommendationAggregationPlan
                    {
                        DefaultAggregation = ResolveAggregation(measure.Name).ToString(),
                        SupportedAggregations = ["Sum", "Avg"]
                    }
                });
            }
        }

        var scatterPairs = BuildScatterPairs(numeric, target);
        foreach (var (x, y) in scatterPairs.Take(4))
        {
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "RelationshipScatterV2",
                Title = $"Relação entre {x.Name} e {y.Name}",
                Reason = "Relação entre medidas numéricas com potencial correlação.",
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Scatter },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = x.Name, Role = AxisRole.Measure },
                    Y = new FieldSpec { Column = y.Name, Role = AxisRole.Measure }
                },
                IncludedColumns = new RecommendationIncludedColumns { X = x.Name, Y = [y.Name] },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = "None",
                    SupportedAggregations = ["None"]
                }
            });
        }

        var multiSeries = BuildMultiSeriesCandidates(numeric, dates, categorical, index, target, ref counter);
        recommendations.AddRange(multiSeries);

        return recommendations;
    }

    private static List<(Domain.ValueObjects.ColumnProfile X, Domain.ValueObjects.ColumnProfile Y)> BuildScatterPairs(
        IReadOnlyList<Domain.ValueObjects.ColumnProfile> numeric,
        Domain.ValueObjects.ColumnProfile? target)
    {
        var pairs = new List<(Domain.ValueObjects.ColumnProfile X, Domain.ValueObjects.ColumnProfile Y)>();
        if (target is not null)
        {
            foreach (var column in numeric.Where(c => !string.Equals(c.Name, target.Name, StringComparison.OrdinalIgnoreCase)).Take(4))
            {
                pairs.Add((column, target));
            }

            return pairs;
        }

        for (var i = 0; i < numeric.Count; i++)
        {
            for (var j = i + 1; j < numeric.Count; j++)
            {
                pairs.Add((numeric[i], numeric[j]));
            }
        }

        return pairs;
    }

    private static List<ChartRecommendation> BuildMultiSeriesCandidates(
        IReadOnlyList<Domain.ValueObjects.ColumnProfile> numeric,
        IReadOnlyList<Domain.ValueObjects.ColumnProfile> dates,
        IReadOnlyList<Domain.ValueObjects.ColumnProfile> categorical,
        DatasetIndex? index,
        Domain.ValueObjects.ColumnProfile? target,
        ref int counter)
    {
        var output = new List<ChartRecommendation>();
        if (numeric.Count < 2)
        {
            return output;
        }

        var dimension = dates.FirstOrDefault() ?? categorical.FirstOrDefault();
        if (dimension is null)
        {
            return output;
        }

        var indexColumns = index?.Columns ?? [];
        var selected = numeric
            .Where(column => IsSameMagnitudeBand(column.Name, target?.Name, indexColumns))
            .Take(3)
            .ToList();

        if (selected.Count < 2)
        {
            selected = numeric.Take(3).ToList();
        }

        if (selected.Count < 2)
        {
            return output;
        }

        var first = selected[0];
        output.Add(new ChartRecommendation
        {
            Id = $"rec_{counter++:D3}",
            TemplateType = "MultiSeriesV2",
            Title = $"Comparativo multi-séries por {dimension.Name}",
            Reason = "Séries combinadas em banda de magnitude similar para leitura comparativa.",
            Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = dates.Count > 0 ? ChartType.Line : ChartType.Bar },
            Query = new ChartQuery
            {
                X = new FieldSpec
                {
                    Column = dimension.Name,
                    Role = dates.Count > 0 ? AxisRole.Time : AxisRole.Category,
                    Bin = dates.Count > 0 ? TimeBin.Month : null
                },
                Y = new FieldSpec
                {
                    Column = first.Name,
                    Role = AxisRole.Measure,
                    Aggregation = ResolveAggregation(first.Name)
                },
                YMetrics = selected.Select(column => new FieldSpec
                {
                    Column = column.Name,
                    Role = AxisRole.Measure,
                    Aggregation = ResolveAggregation(column.Name)
                }).ToList()
            },
            IncludedColumns = new RecommendationIncludedColumns
            {
                X = dimension.Name,
                Y = selected.Select(column => column.Name).ToList()
            },
            AggregationPlan = new RecommendationAggregationPlan
            {
                DefaultAggregation = ResolveAggregation(first.Name).ToString(),
                SupportedAggregations = ["Sum", "Avg"]
            },
            AxisPolicy = new AxisPolicy
            {
                DefaultMode = "SingleAxisByMagnitudeBand",
                MaxAxes = 2,
                SuggestSeparateAxesWhenScaleRatioAbove = 100,
                AllowPerSeriesAxisOverride = true
            }
        });

        return output;
    }

    private static bool IsSameMagnitudeBand(
        string columnName,
        string? targetColumn,
        IReadOnlyCollection<ColumnIndex> columns)
    {
        if (string.IsNullOrWhiteSpace(targetColumn))
        {
            return true;
        }

        var candidate = columns.FirstOrDefault(item =>
            string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
        var target = columns.FirstOrDefault(item =>
            string.Equals(item.Name, targetColumn, StringComparison.OrdinalIgnoreCase));

        var candidateBasis = candidate?.NumericStats?.P50 ?? candidate?.NumericStats?.Mean;
        var targetBasis = target?.NumericStats?.P50 ?? target?.NumericStats?.Mean;

        if (candidateBasis is null || targetBasis is null)
        {
            return true;
        }

        return string.Equals(
            Normalization.BuildMagnitudeBand(candidateBasis),
            Normalization.BuildMagnitudeBand(targetBasis),
            StringComparison.OrdinalIgnoreCase);
    }

    private static Aggregation ResolveAggregation(string columnName)
    {
        return columnName.Contains("count", StringComparison.OrdinalIgnoreCase)
            ? Aggregation.Count
            : columnName.Contains("rate", StringComparison.OrdinalIgnoreCase)
                ? Aggregation.Avg
                : Aggregation.Sum;
    }
}
