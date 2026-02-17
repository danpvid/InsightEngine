using System.Globalization;
using System.Text.Json;
using DuckDB.NET.Data;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class ChartPercentileService : IChartPercentileService
{
    private static readonly List<PercentileKind> AvailableKinds = new()
    {
        PercentileKind.P5,
        PercentileKind.P10,
        PercentileKind.P90,
        PercentileKind.P95
    };

    private readonly ILogger<ChartPercentileService> _logger;

    public ChartPercentileService(ILogger<ChartPercentileService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<ChartPercentileComputationResult>> ComputeAsync(
        string csvPath,
        ChartRecommendation recommendation,
        EChartsOption baseOption,
        ChartViewKind view,
        PercentileMode requestedMode,
        PercentileKind? percentileKind,
        string percentileTarget,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        try
        {
            var percentile = percentileKind ?? PercentileKind.P95;
            var modeResult = await ResolveModeAsync(csvPath, recommendation, requestedMode);
            var meta = new ChartPercentileMeta
            {
                Supported = modeResult.Supported,
                Mode = modeResult.Mode,
                Available = new List<PercentileKind>(AvailableKinds),
                Reason = modeResult.Reason
            };

            var response = new ChartPercentileComputationResult
            {
                Percentiles = meta,
                View = new ChartViewMeta
                {
                    Kind = view
                }
            };

            if (!modeResult.Supported)
            {
                response.View.Kind = ChartViewKind.Base;
                return Result.Success(response);
            }

            var target = string.IsNullOrWhiteSpace(percentileTarget)
                ? "y"
                : percentileTarget.Trim().ToLowerInvariant();
            if (!string.Equals(target, "y", StringComparison.OrdinalIgnoreCase))
            {
                response.View.Kind = ChartViewKind.Base;
                response.Percentiles.Reason = "Only percentileTarget=y is currently supported.";
                return Result.Success(response);
            }

            var overallValues = await ComputeOverallValuesAsync(csvPath, recommendation, cancellationToken);
            if (overallValues.Count > 0)
            {
                response.Percentiles.Values = overallValues
                    .Select(pair => new PercentileValue { Kind = pair.Key, Value = pair.Value })
                    .OrderBy(item => item.Kind)
                    .ToList();
            }

            if (view != ChartViewKind.Percentile)
            {
                response.View.Kind = ChartViewKind.Base;
                return Result.Success(response);
            }

            response.View = new ChartViewMeta
            {
                Kind = ChartViewKind.Percentile,
                PercentileKind = percentile,
                PercentileMode = modeResult.Mode
            };

            var baseClone = CloneOption(baseOption);
            EChartsOption? option = modeResult.Mode switch
            {
                PercentileMode.Bucket => BuildBucketPercentileOption(csvPath, recommendation, baseClone, percentile, cancellationToken),
                PercentileMode.Overall => BuildOverallPercentileOption(baseClone, recommendation, percentile, overallValues),
                _ => null
            };

            if (option == null)
            {
                response.View.Kind = ChartViewKind.Base;
                response.Percentiles.Reason = response.Percentiles.Reason ?? "Percentile view is not available for this chart.";
                return Result.Success(response);
            }

            response.Option = option;
            return Result.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Percentile computation failed for RecommendationId={RecommendationId}",
                recommendation.Id);
            return Result.Failure<ChartPercentileComputationResult>($"Percentile computation failed: {ex.Message}");
        }
    }

    private async Task<(bool Supported, PercentileMode Mode, string? Reason)> ResolveModeAsync(
        string csvPath,
        ChartRecommendation recommendation,
        PercentileMode requestedMode)
    {
        if (recommendation.Chart.Type == ChartType.Line)
        {
            if (requestedMode == PercentileMode.Overall)
            {
                return (true, PercentileMode.Overall, null);
            }

            return (true, PercentileMode.Bucket, null);
        }

        if (recommendation.Chart.Type == ChartType.Bar)
        {
            if (requestedMode == PercentileMode.Bucket || requestedMode == PercentileMode.Overall)
            {
                return (true, requestedMode, null);
            }

            var hasMultipleRows = await HasMultipleRowsPerCategoryAsync(csvPath, recommendation);
            return hasMultipleRows
                ? (true, PercentileMode.Bucket, null)
                : (true, PercentileMode.Overall, "Category buckets have a single value; using overall percentiles.");
        }

        if (recommendation.Chart.Type == ChartType.Scatter)
        {
            return (true, PercentileMode.Overall, null);
        }

        if (recommendation.Chart.Type == ChartType.Histogram)
        {
            return (true, PercentileMode.Overall, "Histogram uses overall percentile references.");
        }

        return (false, PercentileMode.NotApplicable, $"Percentile drilldown is not applicable to chart type {recommendation.Chart.Type}.");
    }

    private EChartsOption? BuildBucketPercentileOption(
        string csvPath,
        ChartRecommendation recommendation,
        EChartsOption option,
        PercentileKind percentileKind,
        CancellationToken cancellationToken)
    {
        var quantile = ToQuantile(percentileKind);
        if (recommendation.Chart.Type == ChartType.Line)
        {
            var points = QueryLineBucketPercentile(csvPath, recommendation, quantile, cancellationToken);
            if (points.Count == 0)
            {
                return null;
            }

            var aligned = AlignLineBuckets(option, points);
            var legendName = $"{percentileKind}(y)";
            option.Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = legendName,
                    ["type"] = "line",
                    ["smooth"] = true,
                    ["connectNulls"] = true,
                    ["lineStyle"] = new Dictionary<string, object>
                    {
                        ["type"] = "dashed",
                        ["width"] = 2,
                        ["color"] = "#dc2626"
                    },
                    ["data"] = aligned.Select(item => new object?[] { item.Bucket, item.Value }).ToList()
                }
            };
            option.Legend = new Dictionary<string, object>
            {
                ["data"] = new List<string> { legendName }
            };
            AppendTitleSuffix(option, $"Percentile view {percentileKind} (bucket)");
            return option;
        }

        if (recommendation.Chart.Type == ChartType.Bar)
        {
            var bucketValues = QueryCategoryBucketPercentile(csvPath, recommendation, quantile, cancellationToken);
            if (bucketValues.Count == 0)
            {
                return null;
            }

            var categories = ReadCategoryAxis(option);
            if (categories.Count == 0)
            {
                categories = bucketValues.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var seriesValues = categories
                .Select(category => bucketValues.TryGetValue(category, out var value) ? value : (double?)null)
                .Cast<object?>()
                .ToList();

            var legendName = $"{percentileKind}(y)";
            option.XAxis ??= new Dictionary<string, object>();
            option.XAxis["data"] = categories;
            option.Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = legendName,
                    ["type"] = "bar",
                    ["data"] = seriesValues,
                    ["itemStyle"] = new Dictionary<string, object>
                    {
                        ["color"] = "#dc2626"
                    }
                }
            };
            option.Legend = new Dictionary<string, object>
            {
                ["data"] = new List<string> { legendName }
            };
            AppendTitleSuffix(option, $"Percentile view {percentileKind} (bucket)");
            return option;
        }

        return null;
    }

    private EChartsOption? BuildOverallPercentileOption(
        EChartsOption option,
        ChartRecommendation recommendation,
        PercentileKind percentileKind,
        IReadOnlyDictionary<PercentileKind, double> values)
    {
        if (!values.TryGetValue(percentileKind, out var percentileValue))
        {
            return null;
        }

        var rounded = Math.Round(percentileValue, 4);

        if (recommendation.Chart.Type == ChartType.Histogram)
        {
            AppendTitleSuffix(option, $"{percentileKind}: {rounded.ToString(CultureInfo.InvariantCulture)}");
            return option;
        }

        if (option.Series == null || option.Series.Count == 0)
        {
            return null;
        }

        var firstSeries = option.Series[0];
        firstSeries["markLine"] = new Dictionary<string, object>
        {
            ["silent"] = true,
            ["symbol"] = new[] { "none", "none" },
            ["lineStyle"] = new Dictionary<string, object>
            {
                ["type"] = "dashed",
                ["width"] = 2,
                ["color"] = "#dc2626"
            },
            ["label"] = new Dictionary<string, object>
            {
                ["show"] = true,
                ["formatter"] = $"{percentileKind} = {rounded.ToString(CultureInfo.InvariantCulture)}"
            },
            ["data"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = percentileKind.ToString(),
                    ["yAxis"] = rounded
                }
            }
        };

        AppendTitleSuffix(option, $"Percentile view {percentileKind} (overall)");
        return option;
    }

    private Dictionary<string, double> QueryCategoryBucketPercentile(
        string csvPath,
        ChartRecommendation recommendation,
        double quantile,
        CancellationToken cancellationToken)
    {
        var xCol = EscapeIdentifier(recommendation.Query.X.Column);
        var yExpr = BuildNumericExpression(recommendation.Query.Y.Column);
        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);

        var sql = $@"
SELECT bucket, quantile_cont(parsed_y, {quantile.ToString(CultureInfo.InvariantCulture)}) AS y
FROM (
    SELECT
        CAST({xCol} AS VARCHAR) AS bucket,
        {yExpr} AS parsed_y
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE {xCol} IS NOT NULL{filterClause}
)
WHERE bucket IS NOT NULL AND parsed_y IS NOT NULL
GROUP BY 1
ORDER BY 1;";

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var bucket = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(bucket))
            {
                continue;
            }

            if (reader.IsDBNull(1))
            {
                continue;
            }

            values[bucket] = reader.GetDouble(1);
        }

        return values;
    }

    private List<(long Bucket, double Value)> QueryLineBucketPercentile(
        string csvPath,
        ChartRecommendation recommendation,
        double quantile,
        CancellationToken cancellationToken)
    {
        var xCol = EscapeIdentifier(recommendation.Query.X.Column);
        var yExpr = BuildNumericExpression(recommendation.Query.Y.Column);
        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);
        var datePart = recommendation.Query.X.Bin switch
        {
            TimeBin.Day => "day",
            TimeBin.Week => "week",
            TimeBin.Month => "month",
            TimeBin.Quarter => "quarter",
            TimeBin.Year => "year",
            _ => "day"
        };
        var parsedDateExpr = BuildParsedDateExpression(xCol);

        var sql = $@"
SELECT
    CAST(epoch(date_trunc('{datePart}', parsed_date)) * 1000 AS BIGINT) AS x,
    quantile_cont(parsed_y, {quantile.ToString(CultureInfo.InvariantCulture)}) AS y
FROM (
    SELECT
        {parsedDateExpr} AS parsed_date,
        {yExpr} AS parsed_y
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE {xCol} IS NOT NULL{filterClause}
)
WHERE parsed_date IS NOT NULL AND parsed_y IS NOT NULL
GROUP BY 1
ORDER BY 1;";

        var points = new List<(long Bucket, double Value)>();
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            points.Add((reader.GetInt64(0), reader.GetDouble(1)));
        }

        return points;
    }

    private async Task<Dictionary<PercentileKind, double>> ComputeOverallValuesAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var metricColumn = recommendation.Chart.Type == ChartType.Histogram
            ? recommendation.Query.X.Column
            : recommendation.Query.Y.Column;
        if (string.IsNullOrWhiteSpace(metricColumn))
        {
            return new Dictionary<PercentileKind, double>();
        }

        var metricExpr = BuildNumericExpression(metricColumn);
        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);

        var sql = $@"
SELECT
    quantile_cont(metric, 0.05) AS p5,
    quantile_cont(metric, 0.10) AS p10,
    quantile_cont(metric, 0.90) AS p90,
    quantile_cont(metric, 0.95) AS p95
FROM (
    SELECT {metricExpr} AS metric
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE {EscapeIdentifier(metricColumn)} IS NOT NULL{filterClause}
)
WHERE metric IS NOT NULL;";

        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || cancellationToken.IsCancellationRequested)
        {
            return new Dictionary<PercentileKind, double>();
        }

        var result = new Dictionary<PercentileKind, double>();
        if (!reader.IsDBNull(0)) result[PercentileKind.P5] = reader.GetDouble(0);
        if (!reader.IsDBNull(1)) result[PercentileKind.P10] = reader.GetDouble(1);
        if (!reader.IsDBNull(2)) result[PercentileKind.P90] = reader.GetDouble(2);
        if (!reader.IsDBNull(3)) result[PercentileKind.P95] = reader.GetDouble(3);
        return result;
    }

    private async Task<bool> HasMultipleRowsPerCategoryAsync(string csvPath, ChartRecommendation recommendation)
    {
        await Task.CompletedTask;

        var xCol = EscapeIdentifier(recommendation.Query.X.Column);
        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);
        var sql = $@"
SELECT COALESCE(MAX(bucket_count), 0)
FROM (
    SELECT CAST({xCol} AS VARCHAR) AS bucket, COUNT(*) AS bucket_count
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE {xCol} IS NOT NULL{filterClause}
    GROUP BY 1
) t;";

        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var scalar = command.ExecuteScalar();
        var maxBucketCount = Convert.ToInt32(scalar ?? 0);
        return maxBucketCount > 1;
    }

    private static List<(long Bucket, double? Value)> AlignLineBuckets(EChartsOption option, List<(long Bucket, double Value)> values)
    {
        var lookup = values.ToDictionary(item => item.Bucket, item => (double?)item.Value);
        var baseBuckets = ExtractLineBuckets(option);
        if (baseBuckets.Count == 0)
        {
            return values.Select(item => (item.Bucket, (double?)item.Value)).ToList();
        }

        return baseBuckets
            .Select(bucket => (bucket, lookup.TryGetValue(bucket, out var value) ? value : (double?)null))
            .ToList();
    }

    private static List<long> ExtractLineBuckets(EChartsOption option)
    {
        var result = new List<long>();
        var firstSeries = option.Series?.FirstOrDefault();
        if (firstSeries == null || !firstSeries.TryGetValue("data", out var rawData))
        {
            return result;
        }

        if (rawData is not IEnumerable<object> points)
        {
            return result;
        }

        foreach (var item in points)
        {
            if (item is object[] tuple && tuple.Length > 0 && TryToInt64(tuple[0], out var bucket))
            {
                result.Add(bucket);
                continue;
            }

            if (item is IEnumerable<object> listItem)
            {
                var first = listItem.FirstOrDefault();
                if (TryToInt64(first, out bucket))
                {
                    result.Add(bucket);
                }
            }
        }

        return result;
    }

    private static bool TryToInt64(object? value, out long parsed)
    {
        parsed = 0;
        if (value == null)
        {
            return false;
        }

        return value switch
        {
            long longValue => (parsed = longValue) == longValue,
            int intValue => (parsed = intValue) == intValue,
            double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue) => (parsed = Convert.ToInt64(doubleValue)) >= long.MinValue,
            decimal decimalValue => (parsed = Convert.ToInt64(decimalValue)) >= long.MinValue,
            string stringValue when long.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var longFromString) => (parsed = longFromString) == longFromString,
            _ => false
        };
    }

    private static List<string> ReadCategoryAxis(EChartsOption option)
    {
        if (option.XAxis == null || !option.XAxis.TryGetValue("data", out var raw) || raw == null)
        {
            return new List<string>();
        }

        if (raw is IEnumerable<object> list)
        {
            return list
                .Select(item => item?.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        return new List<string>();
    }

    private static void AppendTitleSuffix(EChartsOption option, string suffix)
    {
        option.Title ??= new Dictionary<string, object>();
        var existingSubtext = option.Title.TryGetValue("subtext", out var rawSubtext)
            ? rawSubtext?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(existingSubtext))
        {
            option.Title["subtext"] = suffix;
            return;
        }

        if (existingSubtext.Contains(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        option.Title["subtext"] = $"{existingSubtext} | {suffix}";
    }

    private static string BuildParsedDateExpression(string xColumnExpr)
    {
        return $@"COALESCE(
            TRY_CAST({xColumnExpr} AS TIMESTAMP),
            TRY_STRPTIME(CAST({xColumnExpr} AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST({xColumnExpr} AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST({xColumnExpr} AS VARCHAR), '%Y-%m-%d'),
            TRY_STRPTIME(CAST({xColumnExpr} AS VARCHAR), '%m/%d/%Y')
        )";
    }

    private static string BuildNumericExpression(string column)
    {
        var escaped = EscapeIdentifier(column);
        return $"TRY_CAST(REPLACE(CAST({escaped} AS VARCHAR), ',', '') AS DOUBLE)";
    }

    private string BuildFilterClause(IReadOnlyCollection<ChartFilter> filters)
    {
        if (filters.Count == 0)
        {
            return string.Empty;
        }

        var combined = BuildCombinedFilterExpression(filters, BuildFilterExpression);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $" AND {combined}";
    }

    private string BuildFilterExpression(ChartFilter filter)
    {
        if (filter.Values.Count == 0 || string.IsNullOrWhiteSpace(filter.Column))
        {
            return string.Empty;
        }

        var escapedColumn = EscapeIdentifier(filter.Column);
        var columnExpr = $"CAST({escapedColumn} AS VARCHAR)";

        return filter.Operator switch
        {
            FilterOperator.Contains => $"LOWER({columnExpr}) LIKE {ToSqlLiteral($"%{filter.Values[0].ToLowerInvariant()}%")}",
            FilterOperator.Eq => BuildComparison(columnExpr, "=", filter.Values),
            FilterOperator.NotEq => BuildComparison(columnExpr, "<>", filter.Values),
            FilterOperator.Gt => BuildComparison(columnExpr, ">", filter.Values),
            FilterOperator.Gte => BuildComparison(columnExpr, ">=", filter.Values),
            FilterOperator.Lt => BuildComparison(columnExpr, "<", filter.Values),
            FilterOperator.Lte => BuildComparison(columnExpr, "<=", filter.Values),
            FilterOperator.In => BuildInClause(columnExpr, filter.Values),
            FilterOperator.Between => BuildBetweenClause(columnExpr, filter.Values),
            _ => string.Empty
        };
    }

    private string BuildComparison(string columnExpr, string op, List<string> values)
    {
        if (TryParseNumericList(values, out var numbers))
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            return $"{numericExpr} {op} {numbers[0].ToString(CultureInfo.InvariantCulture)}";
        }

        if (TryParseDateList(values, out var dates))
        {
            var parsedDateExpr = BuildParsedDateExpression(columnExpr);
            var timestamp = dates[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return $"{parsedDateExpr} {op} TIMESTAMP {ToSqlLiteral(timestamp)}";
        }

        return $"{columnExpr} {op} {ToSqlLiteral(values[0])}";
    }

    private string BuildInClause(string columnExpr, List<string> values)
    {
        if (TryParseNumericList(values, out var numbers))
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            var list = string.Join(", ", numbers.Select(n => n.ToString(CultureInfo.InvariantCulture)));
            return $"{numericExpr} IN ({list})";
        }

        var literals = string.Join(", ", values.Select(ToSqlLiteral));
        return $"{columnExpr} IN ({literals})";
    }

    private string BuildBetweenClause(string columnExpr, List<string> values)
    {
        if (values.Count < 2)
        {
            return string.Empty;
        }

        if (TryParseNumericList(values, out var numbers) && numbers.Count >= 2)
        {
            var numericExpr = $"TRY_CAST(REPLACE({columnExpr}, ',', '') AS DOUBLE)";
            var ranges = BuildRangeExpressions(
                numbers,
                (left, right) => $"{numericExpr} BETWEEN {left.ToString(CultureInfo.InvariantCulture)} AND {right.ToString(CultureInfo.InvariantCulture)}");

            return ranges.Count == 0
                ? string.Empty
                : ranges.Count == 1
                    ? ranges[0]
                    : $"({string.Join(" OR ", ranges)})";
        }

        if (TryParseDateList(values, out var dates) && dates.Count >= 2)
        {
            var parsedDateExpr = BuildParsedDateExpression(columnExpr);
            var ranges = BuildRangeExpressions(
                dates,
                (left, right) =>
                    $"{parsedDateExpr} BETWEEN TIMESTAMP {ToSqlLiteral(left.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} AND TIMESTAMP {ToSqlLiteral(right.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}");

            return ranges.Count == 0
                ? string.Empty
                : ranges.Count == 1
                    ? ranges[0]
                    : $"({string.Join(" OR ", ranges)})";
        }

        var textRanges = BuildRangeExpressions(
            values,
            (left, right) => $"{columnExpr} BETWEEN {ToSqlLiteral(left)} AND {ToSqlLiteral(right)}");

        return textRanges.Count == 0
            ? string.Empty
            : textRanges.Count == 1
                ? textRanges[0]
                : $"({string.Join(" OR ", textRanges)})";
    }

    private static bool TryParseNumericList(List<string> values, out List<double> numbers)
    {
        numbers = new List<double>();
        foreach (var raw in values)
        {
            var normalized = raw.Replace(",", ".");
            if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                numbers.Clear();
                return false;
            }

            numbers.Add(parsed);
        }

        return numbers.Count > 0;
    }

    private static bool TryParseDateList(IReadOnlyList<string> values, out List<DateTime> dates)
    {
        dates = new List<DateTime>();

        foreach (var raw in values)
        {
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) &&
                !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                dates.Clear();
                return false;
            }

            dates.Add(parsed);
        }

        return dates.Count > 0;
    }

    private static List<string> BuildRangeExpressions<T>(IReadOnlyList<T> values, Func<T, T, string> rangeBuilder)
    {
        var expressions = new List<string>();
        if (values.Count < 2)
        {
            return expressions;
        }

        for (var index = 0; index + 1 < values.Count; index += 2)
        {
            expressions.Add(rangeBuilder(values[index], values[index + 1]));
        }

        return expressions;
    }

    private static string BuildCombinedFilterExpression(
        IReadOnlyCollection<ChartFilter> filters,
        Func<ChartFilter, string> expressionBuilder)
    {
        string? combined = null;

        foreach (var filter in filters)
        {
            var expression = expressionBuilder(filter);
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            if (combined == null)
            {
                combined = $"({expression})";
                continue;
            }

            var logicalOperator = filter.LogicalOperator == FilterLogicalOperator.Or ? "OR" : "AND";
            combined = $"({combined} {logicalOperator} ({expression}))";
        }

        return combined ?? string.Empty;
    }

    private static string ToSqlLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string EscapeIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static double ToQuantile(PercentileKind kind)
    {
        return kind switch
        {
            PercentileKind.P5 => 0.05,
            PercentileKind.P10 => 0.10,
            PercentileKind.P90 => 0.90,
            PercentileKind.P95 => 0.95,
            _ => 0.95
        };
    }

    private static EChartsOption CloneOption(EChartsOption option)
    {
        var json = JsonSerializer.Serialize(option);
        return JsonSerializer.Deserialize<EChartsOption>(json) ?? new EChartsOption();
    }
}
