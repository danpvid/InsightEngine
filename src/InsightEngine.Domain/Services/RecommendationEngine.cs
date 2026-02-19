using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
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

        int recCounter = 1;

        // 1. Time Series (Line) - até 2
        recommendations.AddRange(GenerateTimeSeriesRecommendations(timeColumns, measureColumns, ref recCounter, maxCount: 2));

        // 2. Category vs Measure (Bar) - até 6
        recommendations.AddRange(GenerateCategoryBarRecommendations(categoryColumns, measureColumns, ref recCounter, maxCount: 6));

        // 3. Histogram - até 2
        recommendations.AddRange(GenerateHistogramRecommendations(measureColumns, ref recCounter, maxCount: 2));

        // 4. Scatter - até 2
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
                        Aggregation = Aggregation.Avg
                    }
                },
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
                            Aggregation = Aggregation.Sum
                        },
                        TopN = 20
                    },
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
