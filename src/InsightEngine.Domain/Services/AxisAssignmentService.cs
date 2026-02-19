using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Charts;
using InsightEngine.Domain.Models.ImportSchema;
using System.Collections;

namespace InsightEngine.Domain.Services;

public static class AxisAssignmentService
{
    public static (AxisPolicy Policy, List<SeriesAxisAssignment> Assignments) BuildAssignments(
        ChartRecommendation recommendation,
        EChartsOption option,
        DatasetImportSchema? schema,
        string? targetColumn)
    {
        var policy = recommendation.AxisPolicy ?? new AxisPolicy();
        var metrics = recommendation.Query.YMetrics.Count > 0
            ? recommendation.Query.YMetrics.Select(item => item.Column).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [recommendation.Query.Y.Column];

        if (metrics.Count <= 1)
        {
            var single = metrics.FirstOrDefault() ?? recommendation.Query.Y.Column;
            return (policy, new List<SeriesAxisAssignment>
            {
                new()
                {
                    SeriesName = single,
                    ColumnName = single,
                    SemanticType = ResolveSemanticType(schema, single),
                    YAxisIndex = 0,
                    RecommendedAxisIndex = 0,
                    ScaleRatioToPrimary = 1
                }
            });
        }

        var seriesByName = (option.Series ?? new List<Dictionary<string, object>>())
            .ToDictionary(
                item => $"{item.GetValueOrDefault("name") ?? string.Empty}",
                item => item,
                StringComparer.OrdinalIgnoreCase);

        var primary = ResolvePrimaryMetric(metrics, targetColumn, recommendation.Query.Y.Column);
        var primaryMaxAbs = ResolveSeriesMaxAbs(seriesByName, primary);
        if (primaryMaxAbs <= 0)
        {
            primaryMaxAbs = 1;
        }

        var primarySemantic = ResolveSemanticType(schema, primary);
        var maxAxes = Math.Max(1, policy.MaxAxes);
        var threshold = policy.SuggestSeparateAxesWhenScaleRatioAbove <= 0
            ? 50d
            : policy.SuggestSeparateAxesWhenScaleRatioAbove;

        var assignments = new List<SeriesAxisAssignment>();
        foreach (var metric in metrics)
        {
            var semantic = ResolveSemanticType(schema, metric);
            var seriesMax = ResolveSeriesMaxAbs(seriesByName, metric);
            var ratio = seriesMax <= 0 ? 0 : Math.Abs(seriesMax) / primaryMaxAbs;

            var differsSemantic = !string.Equals(semantic, primarySemantic, StringComparison.OrdinalIgnoreCase);
            var defaultAxis = differsSemantic && maxAxes > 1 ? 1 : 0;
            var recommendedAxis = defaultAxis;

            if (!differsSemantic && ratio >= threshold && maxAxes > 1)
            {
                recommendedAxis = 1;
            }

            assignments.Add(new SeriesAxisAssignment
            {
                SeriesName = metric,
                ColumnName = metric,
                SemanticType = semantic,
                YAxisIndex = defaultAxis,
                RecommendedAxisIndex = recommendedAxis,
                ScaleRatioToPrimary = Math.Round(ratio, 6)
            });
        }

        return (policy, assignments);
    }

    private static string ResolvePrimaryMetric(List<string> metrics, string? targetColumn, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(targetColumn))
        {
            var byTarget = metrics.FirstOrDefault(metric => string.Equals(metric, targetColumn, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(byTarget))
            {
                return byTarget;
            }
        }

        var byFallback = metrics.FirstOrDefault(metric => string.Equals(metric, fallback, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byFallback))
        {
            return byFallback;
        }

        return metrics.First();
    }

    private static string ResolveSemanticType(DatasetImportSchema? schema, string metric)
    {
        var column = schema?.Columns.FirstOrDefault(item => string.Equals(item.Name, metric, StringComparison.OrdinalIgnoreCase));
        var type = column?.ConfirmedType ?? column?.InferredType;

        if (!type.HasValue)
        {
            return "Generic";
        }

        return type.Value switch
        {
            InferredType.Money => "Money",
            InferredType.Percentage => "Percentage",
            _ => "Generic"
        };
    }

    private static double ResolveSeriesMaxAbs(
        Dictionary<string, Dictionary<string, object>> seriesByName,
        string seriesName)
    {
        if (!seriesByName.TryGetValue(seriesName, out var series))
        {
            return 0;
        }

        if (!series.TryGetValue("data", out var dataObj) || dataObj is not IEnumerable rows)
        {
            return 0;
        }

        var maxAbs = 0d;
        foreach (var row in rows)
        {
            double? value = row switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                decimal m => (double)m,
                object[] arr when arr.Length > 1 => TryConvertNumber(arr[^1]),
                IEnumerable seq => TryConvertNumber(seq.Cast<object?>().LastOrDefault()),
                _ => null
            };

            if (!value.HasValue)
            {
                continue;
            }

            var abs = Math.Abs(value.Value);
            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        return maxAbs;
    }

    private static double? TryConvertNumber(object? input)
    {
        return input switch
        {
            null => null,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }
}
