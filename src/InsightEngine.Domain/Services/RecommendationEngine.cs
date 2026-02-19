using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Charts;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Services;

public class RecommendationEngine
{
    private const int MaxRecommendations = 12;
    private static readonly string[] IdLikeNameHints =
    [
        "id", "uuid", "guid", "hash", "key", "token"
    ];

    public List<ChartRecommendation> Generate(DatasetProfile profile)
    {
        var recommendations = new List<ChartRecommendation>();
        var activeColumns = profile.Columns.Where(column => !column.IsIgnored).ToList();
        if (activeColumns.Count == 0)
        {
            activeColumns = profile.Columns;
        }

        var effectiveProfile = new DatasetProfile
        {
            DatasetId = profile.DatasetId,
            RowCount = profile.RowCount,
            SampleSize = profile.SampleSize,
            Columns = activeColumns
        };

        var roles = DetectColumnRoles(effectiveProfile);

        var timeColumns = roles.Where(r => r.Role == AxisRole.Time).ToList();
        var measureColumns = roles.Where(r => r.Role == AxisRole.Measure).ToList();
        var categoryColumns = roles.Where(r => r.Role == AxisRole.Category).ToList();
        var targetRole = !string.IsNullOrWhiteSpace(profile.TargetColumn)
            ? roles.FirstOrDefault(role => string.Equals(role.ColumnName, profile.TargetColumn, StringComparison.OrdinalIgnoreCase))
            : null;

        int recCounter = 1;

        if (targetRole != null)
        {
            recommendations.AddRange(GenerateTargetDrivenTemplates(
                targetRole,
                timeColumns,
                categoryColumns,
                measureColumns,
                ref recCounter));
        }
        else
        {
            recommendations.AddRange(GenerateSemanticTemplatesWithoutTarget(
                timeColumns,
                categoryColumns,
                measureColumns,
                ref recCounter));
        }

        recommendations.AddRange(GenerateCategoryBarRecommendations(categoryColumns, measureColumns, ref recCounter, maxCount: 3));
        recommendations.AddRange(GenerateHistogramRecommendations(measureColumns, ref recCounter, maxCount: 2));
        recommendations.AddRange(GenerateScatterRecommendations(measureColumns, ref recCounter, maxCount: 2));

        ApplyScores(recommendations, effectiveProfile);

        var finalRecommendations = recommendations
            .Select((rec, index) => new { rec, index })
            .OrderByDescending(x => x.rec.Score)
            .ThenByDescending(x => x.rec.ImpactScore)
            .ThenBy(x => x.index)
            .Take(MaxRecommendations)
            .Select(x => x.rec)
            .ToList();

        // Gerar optionTemplate para cada recomendação
        foreach (var rec in finalRecommendations)
        {
            rec.OptionTemplate = EChartsOptionTemplateFactory.Create(rec);
        }

        return finalRecommendations;
    }

    private List<ChartRecommendation> GenerateTargetDrivenTemplates(
        ColumnRole target,
        List<ColumnRole> timeColumns,
        List<ColumnRole> categoryColumns,
        List<ColumnRole> measureColumns,
        ref int counter)
    {
        var recommendations = new List<ChartRecommendation>();

        var bestTime = timeColumns.FirstOrDefault();
        var bestDimensions = categoryColumns
            .Where(column => !column.IsIdLike)
            .OrderByDescending(column => column.DimensionScore)
            .Take(3)
            .ToList();

        var bestMeasures = measureColumns
            .OrderByDescending(column => column.MeasureScore)
            .Take(4)
            .ToList();

        if (target.MeasureSemantic == MeasureSemantic.Money)
        {
            var companionMoneyMeasures = measureColumns
                .Where(column =>
                    !string.Equals(column.ColumnName, target.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                    column.MeasureSemantic == MeasureSemantic.Money)
                .OrderByDescending(column => column.MeasureScore)
                .Take(3)
                .ToList();

            if (companionMoneyMeasures.Count > 0)
            {
                var moneyPackMeasures = new List<ColumnRole> { target };
                moneyPackMeasures.AddRange(companionMoneyMeasures);

                var moneyAxisPolicy = new AxisPolicy
                {
                    DefaultMode = "SingleAxisBySemanticType",
                    MaxAxes = 2,
                    SuggestSeparateAxesWhenScaleRatioAbove = 50,
                    AllowPerSeriesAxisOverride = true
                };

                if (bestTime != null)
                {
                    recommendations.Add(new ChartRecommendation
                    {
                        Id = $"rec_{counter++:D3}",
                        TemplateType = "MoneyPackWithTarget",
                        Title = $"{target.ColumnName}: money pack over time",
                        Reason = "multiple Money measures + target selected",
                        Reasoning = BuildReasoning(target, moneyPackMeasures, bestDimensions, "Combines target and companion money measures with axis-separation suggestions by scale ratio."),
                        Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                        Query = new ChartQuery
                        {
                            X = new FieldSpec { Column = bestTime.ColumnName, Role = AxisRole.Time, Bin = TimeBin.Month },
                            Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Sum },
                            YMetrics = moneyPackMeasures.Select(metric => new FieldSpec
                            {
                                Column = metric.ColumnName,
                                Role = AxisRole.Measure,
                                Aggregation = Aggregation.Sum
                            }).ToList(),
                            YAxisMapping = moneyPackMeasures.ToDictionary(
                                metric => metric.ColumnName,
                                _ => 0,
                                StringComparer.OrdinalIgnoreCase)
                        },
                        IncludedColumns = new RecommendationIncludedColumns
                        {
                            X = bestTime.ColumnName,
                            Y = moneyPackMeasures.Select(metric => metric.ColumnName).ToList()
                        },
                        AggregationPlan = new RecommendationAggregationPlan
                        {
                            DefaultAggregation = "Sum",
                            SupportedAggregations = ["Sum", "Avg"]
                        },
                        AxisPolicy = moneyAxisPolicy
                    });
                }
                else if (bestDimensions.Count > 0)
                {
                    var bestDimension = bestDimensions[0];
                    recommendations.Add(new ChartRecommendation
                    {
                        Id = $"rec_{counter++:D3}",
                        TemplateType = "MoneyPackWithTarget",
                        Title = $"{target.ColumnName} money pack by {bestDimension.ColumnName}",
                        Reason = "multiple Money measures + target selected",
                        Reasoning = BuildReasoning(target, moneyPackMeasures, [bestDimension], "Prioritizes target money measure and companion measures by the strongest categorical breakdown."),
                        Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Bar },
                        Query = new ChartQuery
                        {
                            X = new FieldSpec { Column = bestDimension.ColumnName, Role = AxisRole.Category },
                            Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Sum },
                            YMetrics = moneyPackMeasures.Select(metric => new FieldSpec
                            {
                                Column = metric.ColumnName,
                                Role = AxisRole.Measure,
                                Aggregation = Aggregation.Sum
                            }).ToList(),
                            TopN = 10,
                            YAxisMapping = moneyPackMeasures.ToDictionary(
                                metric => metric.ColumnName,
                                _ => 0,
                                StringComparer.OrdinalIgnoreCase)
                        },
                        IncludedColumns = new RecommendationIncludedColumns
                        {
                            X = bestDimension.ColumnName,
                            Y = moneyPackMeasures.Select(metric => metric.ColumnName).ToList()
                        },
                        AggregationPlan = new RecommendationAggregationPlan
                        {
                            DefaultAggregation = "Sum",
                            SupportedAggregations = ["Sum", "Avg"]
                        },
                        AxisPolicy = moneyAxisPolicy
                    });
                }
            }
        }

        if (bestTime != null && bestMeasures.Count >= 2)
        {
            var selected = bestMeasures.Take(4).ToList();
            var primary = selected[0];

            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "MultiMetricTimeSeries",
                Title = $"{target.ColumnName}: multi-metric trend over time",
                Reason = "Target-focused multivariate time-series recommendation.",
                Reasoning = BuildReasoning(target, selected, bestDimensions, "Combines top semantic measures over time for target monitoring."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = bestTime.ColumnName, Role = AxisRole.Time, Bin = TimeBin.Month },
                    Y = new FieldSpec { Column = primary.ColumnName, Role = AxisRole.Measure, Aggregation = ResolveDefaultAggregation(primary) },
                    YMetrics = selected.Select(metric => new FieldSpec
                    {
                        Column = metric.ColumnName,
                        Role = AxisRole.Measure,
                        Aggregation = ResolveDefaultAggregation(metric)
                    }).ToList()
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = bestTime.ColumnName,
                    Y = selected.Select(metric => metric.ColumnName).ToList()
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = ResolveDefaultAggregation(primary).ToString(),
                    SupportedAggregations = ["Sum", "Avg", "Count", "Min", "Max"]
                }
            });
        }

        if (bestTime != null && bestDimensions.Count > 0)
        {
            var bestDimension = bestDimensions[0];
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "TargetSplitByCategory",
                Title = $"{target.ColumnName} over time by {bestDimension.ColumnName}",
                Reason = "Target split across the strongest dimension over time.",
                Reasoning = BuildReasoning(target, [target], [bestDimension], "Highlights which categories drive target shifts in time."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = bestTime.ColumnName, Role = AxisRole.Time, Bin = TimeBin.Month },
                    Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure, Aggregation = ResolveDefaultAggregation(target) },
                    Series = new FieldSpec { Column = bestDimension.ColumnName, Role = AxisRole.Category },
                    TopN = 6
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = bestTime.ColumnName,
                    Y = [target.ColumnName],
                    Series = bestDimension.ColumnName
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = ResolveDefaultAggregation(target).ToString(),
                    SupportedAggregations = ["Sum", "Avg", "Count"]
                }
            });
        }

        if (bestDimensions.Count > 0)
        {
            var bestDimension = bestDimensions[0];
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "TargetTopNByCategory",
                Title = $"Top categories impacting {target.ColumnName}",
                Reason = "Top-N category ranking with optional Others bucket.",
                Reasoning = BuildReasoning(target, [target], [bestDimension], "Surfaces concentration effects by category and supports Top-N analysis."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Bar },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = bestDimension.ColumnName, Role = AxisRole.Category },
                    Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure, Aggregation = ResolveDefaultAggregation(target) },
                    TopN = 10
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = bestDimension.ColumnName,
                    Y = [target.ColumnName]
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = ResolveDefaultAggregation(target).ToString(),
                    SupportedAggregations = ["Sum", "Avg", "Count"]
                }
            });

            if (target.MeasureSemantic == MeasureSemantic.Money)
            {
                recommendations.Add(new ChartRecommendation
                {
                    Id = $"rec_{counter++:D3}",
                    TemplateType = "Pareto",
                    Title = $"Pareto of negative impact on {target.ColumnName}",
                    Reason = "Money target enables Pareto contribution analysis.",
                    Reasoning = BuildReasoning(target, [target], [bestDimension], "Ranks categories by impact and cumulative contribution for focus."),
                    Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Bar },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec { Column = bestDimension.ColumnName, Role = AxisRole.Category },
                        Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Sum },
                        TopN = 12
                    },
                    IncludedColumns = new RecommendationIncludedColumns
                    {
                        X = bestDimension.ColumnName,
                        Y = [target.ColumnName]
                    },
                    AggregationPlan = new RecommendationAggregationPlan
                    {
                        DefaultAggregation = "Sum",
                        SupportedAggregations = ["Sum"]
                    }
                });
            }
        }

        var scatterCandidate = measureColumns
            .Where(column => !string.Equals(column.ColumnName, target.ColumnName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(column => column.MeasureScore)
            .FirstOrDefault();

        if (scatterCandidate != null)
        {
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "TargetScatter",
                Title = $"{target.ColumnName} vs {scatterCandidate.ColumnName}",
                Reason = "Scatter recommendation for target-feature relationship.",
                Reasoning = BuildReasoning(target, [target, scatterCandidate], bestDimensions, "Shows relationship and potential trendline between target and feature."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Scatter },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = scatterCandidate.ColumnName, Role = AxisRole.Measure },
                    Y = new FieldSpec { Column = target.ColumnName, Role = AxisRole.Measure }
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = scatterCandidate.ColumnName,
                    Y = [target.ColumnName]
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = "None",
                    SupportedAggregations = ["None"]
                }
            });
        }

        var moneyMeasure = measureColumns.FirstOrDefault(column => column.MeasureSemantic == MeasureSemantic.Money);
        var percentageMeasure = measureColumns.FirstOrDefault(column => column.MeasureSemantic == MeasureSemantic.Percentage);
        if (bestTime != null && moneyMeasure != null && percentageMeasure != null)
        {
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "DualAxisMoneyPercentage",
                Title = $"{moneyMeasure.ColumnName} and {percentageMeasure.ColumnName} over time",
                Reason = "Dual-axis template mixing Money and Percentage measures.",
                Reasoning = BuildReasoning(target, [moneyMeasure, percentageMeasure], bestDimensions, "Combines financial volume and rate behavior in one trend."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = bestTime.ColumnName, Role = AxisRole.Time, Bin = TimeBin.Month },
                    Y = new FieldSpec { Column = moneyMeasure.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Sum },
                    YMetrics =
                    [
                        new FieldSpec { Column = moneyMeasure.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Sum },
                        new FieldSpec { Column = percentageMeasure.ColumnName, Role = AxisRole.Measure, Aggregation = Aggregation.Avg }
                    ],
                    YAxisMapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        [moneyMeasure.ColumnName] = 0,
                        [percentageMeasure.ColumnName] = 1
                    }
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = bestTime.ColumnName,
                    Y = [moneyMeasure.ColumnName, percentageMeasure.ColumnName]
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = "Sum/Avg",
                    SupportedAggregations = ["Sum", "Avg"]
                }
            });
        }

        return recommendations;
    }

    private List<ChartRecommendation> GenerateSemanticTemplatesWithoutTarget(
        List<ColumnRole> timeColumns,
        List<ColumnRole> categoryColumns,
        List<ColumnRole> measureColumns,
        ref int counter)
    {
        var recommendations = new List<ChartRecommendation>();
        var bestTime = timeColumns.FirstOrDefault();
        var bestMeasures = measureColumns.OrderByDescending(column => column.MeasureScore).Take(4).ToList();

        if (bestTime != null && bestMeasures.Count >= 2)
        {
            var primary = bestMeasures[0];
            recommendations.Add(new ChartRecommendation
            {
                Id = $"rec_{counter++:D3}",
                TemplateType = "MultiMetricTimeSeries",
                Title = "Top measures over time",
                Reason = "Semantic multi-metric trend without explicit target.",
                Reasoning = BuildReasoning(primary, bestMeasures, categoryColumns, "Compares highest-value measures over common time grain."),
                Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = bestTime.ColumnName, Role = AxisRole.Time, Bin = TimeBin.Month },
                    Y = new FieldSpec { Column = primary.ColumnName, Role = AxisRole.Measure, Aggregation = ResolveDefaultAggregation(primary) },
                    YMetrics = bestMeasures.Select(metric => new FieldSpec
                    {
                        Column = metric.ColumnName,
                        Role = AxisRole.Measure,
                        Aggregation = ResolveDefaultAggregation(metric)
                    }).ToList()
                },
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = bestTime.ColumnName,
                    Y = bestMeasures.Select(column => column.ColumnName).ToList()
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = ResolveDefaultAggregation(primary).ToString(),
                    SupportedAggregations = ["Sum", "Avg", "Count"]
                }
            });
        }

        return recommendations;
    }

    private static void ApplyScores(List<ChartRecommendation> recommendations, DatasetProfile profile)
    {
        var columns = profile.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var sampleSize = Math.Max(1, profile.SampleSize);
        var lowCardinalityLimit = Math.Max(20, (int)(sampleSize * 0.05));

        foreach (var recommendation in recommendations)
        {
            var criteria = new List<string>();
            var score = GetChartTypeBaseScore(recommendation.Chart.Type, criteria);

            if (columns.TryGetValue(recommendation.Query.Y.Column, out var yColumn))
            {
                var coverage = Math.Clamp(1.0 - yColumn.NullRate, 0.0, 1.0);
                var distinctRatio = Math.Clamp(yColumn.DistinctCount / (double)sampleSize, 0.0, 1.0);

                score += 0.18 * coverage;
                score += 0.22 * distinctRatio;

                criteria.Add($"Y coverage {(coverage * 100):F0}%");
                criteria.Add($"Y distinct ratio {(distinctRatio * 100):F0}%");
            }

            if (columns.TryGetValue(recommendation.Query.X.Column, out var xColumn))
            {
                if (xColumn.InferredType == InferredType.Date)
                {
                    score += 0.14;
                    criteria.Add("Time dimension detected");
                }
                else if (xColumn.DistinctCount <= lowCardinalityLimit)
                {
                    score += 0.08;
                    criteria.Add($"Low-cardinality X ({xColumn.DistinctCount})");
                }
                else
                {
                    score -= 0.04;
                    criteria.Add($"High-cardinality X ({xColumn.DistinctCount})");
                }
            }

            if (recommendation.Query.Y.Aggregation == Aggregation.Sum ||
                recommendation.Query.Y.Aggregation == Aggregation.Avg)
            {
                score += 0.05;
                criteria.Add($"Aggregation {recommendation.Query.Y.Aggregation}");
            }

            score = Math.Clamp(score, 0.0, 1.0);
            recommendation.Score = Math.Round(score, 3);

            var impactScore = ComputeImpactScore(recommendation, columns, sampleSize, lowCardinalityLimit, criteria);
            recommendation.ImpactScore = Math.Round(Math.Clamp(impactScore, 0.0, 1.0), 3);
            recommendation.ScoreCriteria = criteria.Take(5).ToList();
        }
    }

    private static double GetChartTypeBaseScore(ChartType chartType, List<string> criteria)
    {
        var baseScore = chartType switch
        {
            ChartType.Line => 0.52,
            ChartType.Bar => 0.5,
            ChartType.Scatter => 0.46,
            ChartType.Histogram => 0.42,
            _ => 0.4
        };

        criteria.Add($"Chart type {chartType}");
        return baseScore;
    }

    private static double ComputeImpactScore(
        ChartRecommendation recommendation,
        Dictionary<string, ColumnProfile> columns,
        int sampleSize,
        int lowCardinalityLimit,
        List<string> criteria)
    {
        var impact = recommendation.Chart.Type switch
        {
            ChartType.Line => 0.45,
            ChartType.Scatter => 0.42,
            ChartType.Bar => 0.38,
            ChartType.Histogram => 0.34,
            _ => 0.32
        };

        if (columns.TryGetValue(recommendation.Query.Y.Column, out var yColumn))
        {
            var distinctRatio = Math.Clamp(yColumn.DistinctCount / (double)sampleSize, 0.0, 1.0);
            var completeness = Math.Clamp(1.0 - yColumn.NullRate, 0.0, 1.0);
            impact += (distinctRatio * 0.3) + (completeness * 0.15);
        }

        if (recommendation.Query.Series != null)
        {
            impact += 0.08;
            criteria.Add("Grouped view enabled");
        }

        if (columns.TryGetValue(recommendation.Query.X.Column, out var xColumn))
        {
            if (xColumn.InferredType == InferredType.Date)
            {
                impact += 0.08;
            }
            else if (xColumn.DistinctCount <= lowCardinalityLimit)
            {
                impact += 0.04;
            }
        }

        return impact;
    }

    private List<ColumnRole> DetectColumnRoles(DatasetProfile profile)
    {
        var roles = new List<ColumnRole>();
        var sampleSize = Math.Max(1, profile.SampleSize);

        foreach (var column in profile.Columns)
        {
            var normalizedType = (column.ConfirmedType ?? column.InferredType).NormalizeLegacy();
            var isIdLike = IsIdColumn(column, sampleSize);
            var semantic = ComputeMeasureSemantic(column.Name, normalizedType);

            var role = new ColumnRole
            {
                ColumnName = column.Name,
                InferredType = normalizedType,
                DistinctCount = column.DistinctCount,
                IsIdLike = isIdLike,
                MeasureSemantic = semantic,
                MeasureScore = ComputeMeasureScore(column, semantic, sampleSize, isIdLike),
                DimensionScore = ComputeDimensionScore(column, sampleSize, isIdLike)
            };

            // Detectar papel
            if (normalizedType == InferredType.Date)
            {
                role.Role = AxisRole.Time;
                role.RoleHint = ColumnRoleHint.Time;
            }
            else if (isIdLike)
            {
                role.Role = AxisRole.Id;
                role.RoleHint = ColumnRoleHint.IdLike;
            }
            else if (normalizedType.IsNumericLike())
            {
                role.Role = AxisRole.Measure;
                role.RoleHint = ColumnRoleHint.Measure;
            }
            else if (normalizedType == InferredType.Category ||
                     normalizedType == InferredType.Boolean ||
                     (normalizedType == InferredType.String && column.DistinctCount <= Math.Max(20, profile.SampleSize * 0.05)))
            {
                role.Role = AxisRole.Category;
                role.RoleHint = ColumnRoleHint.Dimension;
            }
            else
            {
                role.Role = AxisRole.Category; // Default
                role.RoleHint = ColumnRoleHint.Noise;
            }

            roles.Add(role);
        }

        return roles;
    }

    private bool IsIdColumn(ColumnProfile column, int sampleSize)
    {
        // Nome contém pistas de identificador
        if (IsIdLikeNameHint(column.Name))
        {
            return true;
        }

        // Alta cardinalidade (>= 98% único), mas APENAS para tipos String/Category
        // Numbers e Dates com alta cardinalidade não são IDs
        if (column.InferredType == InferredType.String || 
            column.InferredType == InferredType.Category)
        {
            if (column.DistinctCount >= sampleSize * 0.98)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdLikeNameHint(string columnName)
    {
        return IdLikeNameHints.Any(hint =>
            columnName.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static MeasureSemantic ComputeMeasureSemantic(string columnName, InferredType inferredType)
    {
        var normalizedName = columnName.Trim();
        if (inferredType == InferredType.Money || normalizedName.Contains("price", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("amount", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("revenue", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("cost", StringComparison.OrdinalIgnoreCase))
        {
            return MeasureSemantic.Money;
        }

        if (inferredType == InferredType.Percentage)
        {
            return MeasureSemantic.Percentage;
        }

        if (normalizedName.Contains("count", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("qty", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("quant", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Contains("volume", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedName.Contains("count", StringComparison.OrdinalIgnoreCase)
                ? MeasureSemantic.Count
                : MeasureSemantic.Quantity;
        }

        return MeasureSemantic.Generic;
    }

    private static double ComputeMeasureScore(
        ColumnProfile column,
        MeasureSemantic semantic,
        int sampleSize,
        bool isIdLike)
    {
        var distinctRatio = Math.Clamp(column.DistinctCount / (double)Math.Max(1, sampleSize), 0.0, 1.0);
        var varianceWeight = distinctRatio;
        var completenessWeight = Math.Clamp(1.0 - column.NullRate, 0.0, 1.0);
        var semanticWeight = semantic switch
        {
            MeasureSemantic.Money => 1.0,
            MeasureSemantic.Percentage => 0.9,
            _ => 0.75
        };
        var idNoisePenalty = isIdLike ? 0.75 : 0.0;

        var score = (0.45 * varianceWeight) + (0.3 * completenessWeight) + (0.35 * semanticWeight) - idNoisePenalty;
        return Math.Round(Math.Clamp(score, 0.0, 1.0), 4);
    }

    private static double ComputeDimensionScore(ColumnProfile column, int sampleSize, bool isIdLike)
    {
        var distinctRatio = Math.Clamp(column.DistinctCount / (double)Math.Max(1, sampleSize), 0.0, 1.0);
        var moderateCardinalityWeight = 1.0 - Math.Abs(distinctRatio - 0.2);
        moderateCardinalityWeight = Math.Clamp(moderateCardinalityWeight, 0.0, 1.0);

        var completenessWeight = Math.Clamp(1.0 - column.NullRate, 0.0, 1.0);
        var nonTextPenalty = column.InferredType == InferredType.String && distinctRatio > 0.85 ? 0.3 : 0.0;
        var idPenalty = isIdLike ? 0.85 : 0.0;

        var score = (0.5 * moderateCardinalityWeight) + (0.35 * completenessWeight) - nonTextPenalty - idPenalty;
        return Math.Round(Math.Clamp(score, 0.0, 1.0), 4);
    }

    private List<ChartRecommendation> GenerateTimeSeriesRecommendations(
        List<ColumnRole> timeColumns,
        List<ColumnRole> measureColumns,
        ref int counter,
        int maxCount)
    {
        var recommendations = new List<ChartRecommendation>();

        if (!timeColumns.Any() || !measureColumns.Any())
            return recommendations;

        // Preferência: createdAt primeiro
        var preferredTimeColumn = timeColumns.FirstOrDefault(t => t.ColumnName.Contains("created", StringComparison.OrdinalIgnoreCase))
                                  ?? timeColumns.First();

        // Escolher até 2 medidas
        var selectedMeasures = SelectPreferredMeasures(measureColumns).Take(maxCount).ToList();

        foreach (var measure in selectedMeasures)
        {
            if (recommendations.Count >= maxCount) break;

            var rec = new ChartRecommendation
            {
                Id = $"rec_{counter:D3}",
                Title = $"{measure.ColumnName} over time",
                Reason = "Time column + numeric measure: time series with daily average.",
                Chart = new ChartMeta
                {
                    Library = ChartLibrary.ECharts,
                    Type = ChartType.Line
                },
                Query = new ChartQuery
                {
                    X = new FieldSpec
                    {
                        Column = preferredTimeColumn.ColumnName,
                        Role = AxisRole.Time,
                        Bin = TimeBin.Day
                    },
                    Y = new FieldSpec
                    {
                        Column = measure.ColumnName,
                        Role = AxisRole.Measure,
                            Aggregation = ResolveDefaultAggregation(measure)
                    }
                },
                    TemplateType = "TimeSeries",
                    IncludedColumns = new RecommendationIncludedColumns
                    {
                        X = preferredTimeColumn.ColumnName,
                        Y = [measure.ColumnName]
                    },
                    AggregationPlan = new RecommendationAggregationPlan
                    {
                        DefaultAggregation = ResolveDefaultAggregation(measure).ToString(),
                        SupportedAggregations = ["Sum", "Avg", "Count"]
                    },
                    Reasoning = BuildReasoning(measure, [measure], [], "Single metric trend line for baseline monitoring."),
                OptionTemplate = EChartsTemplates.CreateLineTemplate()
            };

            recommendations.Add(rec);
            counter++;
        }

        return recommendations;
    }

    private List<ChartRecommendation> GenerateCategoryBarRecommendations(
        List<ColumnRole> categoryColumns,
        List<ColumnRole> measureColumns,
        ref int counter,
        int maxCount)
    {
        var recommendations = new List<ChartRecommendation>();

        if (!categoryColumns.Any() || !measureColumns.Any())
            return recommendations;

        // Filtrar categorias com distinctCount <= 20
        var validCategories = categoryColumns
            .Where(c => c.DistinctCount <= 20)
            .OrderByDescending(c => c.DimensionScore)
            .Take(3)
            .ToList();
        var selectedMeasures = SelectPreferredMeasures(measureColumns).Take(2).ToList();

        foreach (var category in validCategories)
        {
            foreach (var measure in selectedMeasures)
            {
                if (recommendations.Count >= maxCount) break;

                var rec = new ChartRecommendation
                {
                    Id = $"rec_{counter:D3}",
                    Title = $"{measure.ColumnName} by {category.ColumnName}",
                    Reason = "Low cardinality category + numeric measure: bar chart with sum aggregation.",
                    Chart = new ChartMeta
                    {
                        Library = ChartLibrary.ECharts,
                        Type = ChartType.Bar
                    },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec
                        {
                            Column = category.ColumnName,
                            Role = AxisRole.Category
                        },
                        Y = new FieldSpec
                        {
                            Column = measure.ColumnName,
                            Role = AxisRole.Measure,
                            Aggregation = ResolveDefaultAggregation(measure)
                        },
                        TopN = 20
                    },
                    TemplateType = "CategoryVsMeasure",
                    IncludedColumns = new RecommendationIncludedColumns
                    {
                        X = category.ColumnName,
                        Y = [measure.ColumnName]
                    },
                    AggregationPlan = new RecommendationAggregationPlan
                    {
                        DefaultAggregation = ResolveDefaultAggregation(measure).ToString(),
                        SupportedAggregations = ["Sum", "Avg", "Count", "Min", "Max"]
                    },
                    Reasoning = BuildReasoning(measure, [measure], [category], "Category distribution on selected measure with Top-N filtering."),
                    OptionTemplate = EChartsTemplates.CreateBarTemplate()
                };

                recommendations.Add(rec);
                counter++;
            }
            if (recommendations.Count >= maxCount) break;
        }

        return recommendations;
    }

    private List<ChartRecommendation> GenerateHistogramRecommendations(
        List<ColumnRole> measureColumns,
        ref int counter,
        int maxCount)
    {
        var recommendations = new List<ChartRecommendation>();

        var selectedMeasures = SelectPreferredMeasures(measureColumns).Take(maxCount).ToList();

        foreach (var measure in selectedMeasures)
        {
            if (recommendations.Count >= maxCount) break;

            var rec = new ChartRecommendation
            {
                Id = $"rec_{counter:D3}",
                Title = $"Distribution of {measure.ColumnName}",
                Reason = "Numeric measure: histogram showing distribution.",
                Chart = new ChartMeta
                {
                    Library = ChartLibrary.ECharts,
                    Type = ChartType.Histogram
                },
                Query = new ChartQuery
                {
                    X = new FieldSpec
                    {
                        Column = measure.ColumnName,
                        Role = AxisRole.Measure
                    }
                    // Histogram não usa Y - calcula frequência automaticamente
                },
                TemplateType = "Histogram",
                IncludedColumns = new RecommendationIncludedColumns
                {
                    X = measure.ColumnName,
                    Y = [measure.ColumnName]
                },
                AggregationPlan = new RecommendationAggregationPlan
                {
                    DefaultAggregation = "Distribution",
                    SupportedAggregations = ["Distribution"]
                },
                Reasoning = BuildReasoning(measure, [measure], [], "Shape and spread analysis for numeric metric."),
                OptionTemplate = EChartsTemplates.CreateHistogramTemplate()
            };

            recommendations.Add(rec);
            counter++;
        }

        return recommendations;
    }

    private List<ChartRecommendation> GenerateScatterRecommendations(
        List<ColumnRole> measureColumns,
        ref int counter,
        int maxCount)
    {
        var recommendations = new List<ChartRecommendation>();

        if (measureColumns.Count < 2)
            return recommendations;

        var selectedMeasures = SelectPreferredMeasures(measureColumns).Take(3).ToList();

        // Gerar pares
        for (int i = 0; i < selectedMeasures.Count - 1 && recommendations.Count < maxCount; i++)
        {
            for (int j = i + 1; j < selectedMeasures.Count && recommendations.Count < maxCount; j++)
            {
                var measure1 = selectedMeasures[i];
                var measure2 = selectedMeasures[j];

                var rec = new ChartRecommendation
                {
                    Id = $"rec_{counter:D3}",
                    Title = $"{measure1.ColumnName} vs {measure2.ColumnName}",
                    Reason = "Two numeric measures: scatter plot showing correlation.",
                    Chart = new ChartMeta
                    {
                        Library = ChartLibrary.ECharts,
                        Type = ChartType.Scatter
                    },
                    Query = new ChartQuery
                    {
                        X = new FieldSpec
                        {
                            Column = measure1.ColumnName,
                            Role = AxisRole.Measure
                        },
                        Y = new FieldSpec
                        {
                            Column = measure2.ColumnName,
                            Role = AxisRole.Measure
                        }
                    },
                    TemplateType = "Scatter",
                    IncludedColumns = new RecommendationIncludedColumns
                    {
                        X = measure1.ColumnName,
                        Y = [measure2.ColumnName]
                    },
                    AggregationPlan = new RecommendationAggregationPlan
                    {
                        DefaultAggregation = "None",
                        SupportedAggregations = ["None"]
                    },
                    Reasoning = BuildReasoning(measure2, [measure1, measure2], [], "Pairwise relationship view between top numeric measures."),
                    OptionTemplate = EChartsTemplates.CreateScatterTemplate()
                };

                recommendations.Add(rec);
                counter++;
            }
        }

        return recommendations;
    }

    private List<ColumnRole> SelectPreferredMeasures(List<ColumnRole> measures)
    {
        // Ordem de preferência: score, balance, depois por distinctCount desc
        var ordered = measures
            .OrderByDescending(m => m.MeasureScore)
            .ThenByDescending(m => m.MeasureSemantic == MeasureSemantic.Money ? 1 : 0)
            .ThenByDescending(m => m.MeasureSemantic == MeasureSemantic.Percentage ? 1 : 0)
            .ThenByDescending(m => m.ColumnName.Contains("score", StringComparison.OrdinalIgnoreCase) ? 3 : 0)
            .ThenByDescending(m => m.ColumnName.Contains("balance", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
            .ThenByDescending(m => m.DistinctCount)
            .ToList();

        return ordered;
    }

    private static Aggregation ResolveDefaultAggregation(ColumnRole measure)
    {
        return measure.MeasureSemantic switch
        {
            MeasureSemantic.Money => Aggregation.Sum,
            MeasureSemantic.Percentage => Aggregation.Avg,
            _ => measure.ColumnName.Contains("count", StringComparison.OrdinalIgnoreCase)
                || measure.ColumnName.Contains("total", StringComparison.OrdinalIgnoreCase)
                ? Aggregation.Sum
                : Aggregation.Avg
        };
    }

    private static string BuildReasoning(
        ColumnRole target,
        List<ColumnRole> measures,
        List<ColumnRole> dimensions,
        string summary)
    {
        var measureNames = string.Join(", ", measures.Select(measure =>
            $"{measure.ColumnName}({measure.MeasureSemantic}, score={measure.MeasureScore:F2})"));
        var dimNames = dimensions.Any()
            ? string.Join(", ", dimensions.Take(2).Select(dimension =>
                $"{dimension.ColumnName}(cardinality={dimension.DistinctCount}, score={dimension.DimensionScore:F2})"))
            : "none";

        return $"Target={target.ColumnName}; Measures=[{measureNames}]; Dimensions=[{dimNames}]. {summary}";
    }
}

// Helper class for ECharts templates
public static class EChartsTemplates
{
    public static Dictionary<string, object> CreateLineTemplate()
    {
        return new Dictionary<string, object>
        {
            { "tooltip", new { trigger = "axis" } },
            { "xAxis", new { type = "time" } },
            { "yAxis", new { type = "value" } },
            { "series", new[] { new { type = "line", smooth = true, data = new object[] { } } } }
        };
    }

    public static Dictionary<string, object> CreateBarTemplate()
    {
        return new Dictionary<string, object>
        {
            { "tooltip", new { trigger = "axis" } },
            { "xAxis", new { type = "category", data = new object[] { } } },
            { "yAxis", new { type = "value" } },
            { "series", new[] { new { type = "bar", data = new object[] { } } } }
        };
    }

    public static Dictionary<string, object> CreateHistogramTemplate()
    {
        return new Dictionary<string, object>
        {
            { "tooltip", new { trigger = "axis" } },
            { "xAxis", new { type = "category", data = new object[] { } } },
            { "yAxis", new { type = "value" } },
            { "series", new[] { new { type = "bar", data = new object[] { } } } }
        };
    }

    public static Dictionary<string, object> CreateScatterTemplate()
    {
        return new Dictionary<string, object>
        {
            { "tooltip", new { trigger = "item" } },
            { "xAxis", new { type = "value" } },
            { "yAxis", new { type = "value" } },
            { "series", new[] { new { type = "scatter", data = new object[] { } } } }
        };
    }
}
