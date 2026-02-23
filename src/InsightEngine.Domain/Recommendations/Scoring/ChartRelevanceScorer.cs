using System.Text.RegularExpressions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Recommendations.Scoring;

public class ChartRelevanceScorer
{
    private static readonly Regex SemanticMeasureRegex = new(
        "revenue|cost|amount|total|count|qty|quantity|valor|receita|custo|lucro|faturamento",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public (double Score, ScoreComponents Components) Score(
        ChartRecommendation recommendation,
        DatasetIndex? index,
        RecommendationWeights weights)
    {
        var components = new ScoreComponents();
        if (index is null || index.Columns.Count == 0)
        {
            components.FinalScore = 0.5;
            return (components.FinalScore, components);
        }

        var primaryColumns = ExtractPrimaryColumns(recommendation);
        if (primaryColumns.Count == 0)
        {
            components.FinalScore = 0.5;
            return (components.FinalScore, components);
        }

        var targetColumn = index.TargetColumn;
        var variancePopulation = index.Columns
            .Select(column => column.NumericStats?.P90 ?? column.NumericStats?.StdDev ?? 0)
            .ToList();

        var correlationValues = new List<double>();
        var varianceValues = new List<double>();
        var completenessValues = new List<double>();
        var outlierValues = new List<double>();
        var temporalValues = new List<double>();
        var roleHintValues = new List<double>();
        var semanticValues = new List<double>();
        var cardinalityPenaltyValues = new List<double>();
        var nearConstantPenaltyValues = new List<double>();

        foreach (var columnName in primaryColumns)
        {
            var column = index.Columns.FirstOrDefault(item =>
                string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                continue;
            }

            correlationValues.Add(ResolveCorrelationNorm(index, targetColumn, column.Name));

            var varianceBasis = column.NumericStats?.P90 ?? column.NumericStats?.StdDev;
            varianceValues.Add(Normalization.RobustVarianceNormalize(varianceBasis, variancePopulation));

            completenessValues.Add(Normalization.Clamp01(1d - column.NullRate));

            var outlierRate = ResolveOutlierRate(column);
            outlierValues.Add(Normalization.Clamp01(1d - outlierRate));

            temporalValues.Add(column.DateStats is not null || column.InferredType == InferredType.Date ? 1d : 0d);
            roleHintValues.Add(ResolveRoleHintBonus(column, targetColumn));
            semanticValues.Add(ResolveSemanticHintBonus(column));
            cardinalityPenaltyValues.Add(Normalization.CardinalityPenalty(column.DistinctCount, index.RowCount));
            nearConstantPenaltyValues.Add(ResolveNearConstantPenalty(column));
        }

        components.Correlation = Average(correlationValues);
        components.Variance = Average(varianceValues);
        components.Completeness = Average(completenessValues);
        components.Outlier = Average(outlierValues);
        components.Temporal = Average(temporalValues);
        components.RoleHint = Average(roleHintValues);
        components.SemanticHint = Average(semanticValues);
        components.CardinalityPenalty = Average(cardinalityPenaltyValues);
        components.NearConstantPenalty = Average(nearConstantPenaltyValues);

        var final =
            (weights.Correlation * components.Correlation)
            + (weights.Variance * components.Variance)
            + (weights.Completeness * components.Completeness)
            + (weights.Outlier * components.Outlier)
            + (weights.Temporal * components.Temporal)
            + (weights.RoleHint * components.RoleHint)
            + (weights.SemanticHint * components.SemanticHint)
            - (weights.CardinalityPenalty * components.CardinalityPenalty)
            - (weights.NearConstantPenalty * components.NearConstantPenalty);

        components.FinalScore = Normalization.Clamp01(final);
        return (components.FinalScore, components);
    }

    private static List<string> ExtractPrimaryColumns(ChartRecommendation recommendation)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(recommendation.Query.X.Column))
        {
            columns.Add(recommendation.Query.X.Column);
        }

        if (!string.IsNullOrWhiteSpace(recommendation.Query.Y.Column))
        {
            columns.Add(recommendation.Query.Y.Column);
        }

        if (recommendation.Query.YMetrics is not null)
        {
            foreach (var metric in recommendation.Query.YMetrics)
            {
                if (!string.IsNullOrWhiteSpace(metric.Column))
                {
                    columns.Add(metric.Column);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(recommendation.Query.Series?.Column))
        {
            columns.Add(recommendation.Query.Series.Column);
        }

        return columns.ToList();
    }

    private static double ResolveCorrelationNorm(DatasetIndex index, string? targetColumn, string columnName)
    {
        if (string.IsNullOrWhiteSpace(targetColumn)
            || string.Equals(targetColumn, columnName, StringComparison.OrdinalIgnoreCase))
        {
            return 0.5;
        }

        var edge = index.Correlations.Edges.FirstOrDefault(item =>
            (string.Equals(item.LeftColumn, targetColumn, StringComparison.OrdinalIgnoreCase)
             && string.Equals(item.RightColumn, columnName, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(item.RightColumn, targetColumn, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.LeftColumn, columnName, StringComparison.OrdinalIgnoreCase)));

        if (edge is null)
        {
            return 0.5;
        }

        return Normalization.Clamp01(Math.Abs(edge.Score));
    }

    private static double ResolveOutlierRate(ColumnIndex column)
    {
        if (column.NumericStats is null)
        {
            return 0;
        }

        if (column.NumericStats.P95 is null || column.NumericStats.P50 is null)
        {
            return 0;
        }

        var spread = Math.Abs(column.NumericStats.P95.Value - column.NumericStats.P50.Value);
        var baseline = Math.Abs(column.NumericStats.P50.Value) + 1d;
        var ratio = spread / baseline;
        return Normalization.Clamp01(ratio / 10d);
    }

    private static double ResolveRoleHintBonus(ColumnIndex column, string? targetColumn)
    {
        if (!string.IsNullOrWhiteSpace(targetColumn)
            && string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (column.DateStats is not null || column.InferredType == InferredType.Date)
        {
            return 0.8;
        }

        if (column.NumericStats is not null)
        {
            return 0.7;
        }

        return 0.2;
    }

    private static double ResolveSemanticHintBonus(ColumnIndex column)
    {
        if (column.SemanticTags.Any(tag =>
                tag.Contains("target", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("measure", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("time", StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        return SemanticMeasureRegex.IsMatch(column.Name) ? 0.8 : 0.2;
    }

    private static double ResolveNearConstantPenalty(ColumnIndex column)
    {
        if (column.DistinctCount <= 1)
        {
            return 1;
        }

        if (column.NumericStats?.StdDev is null)
        {
            return 0;
        }

        return column.NumericStats.StdDev.Value <= 1e-9 ? 1 : 0;
    }

    private static double Average(IReadOnlyCollection<double> values)
    {
        return values.Count == 0 ? 0.5 : values.Average();
    }
}
