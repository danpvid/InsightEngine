using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Infra.Data.Services;

internal static class ChartQueryBuilder
{
    public static string BuildTimeSeriesSql(string csvPath, ChartRecommendation recommendation, string filterClause)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var bin = recommendation.Query.X.Bin!.Value;
        var agg = recommendation.Query.Y.Aggregation!.Value;
        var seriesCol = recommendation.Query.Series?.Column;

        var dateTruncPart = MapTimeBin(bin);
        var aggFunction = MapAggregation(agg);
        var escapedPath = csvPath.Replace("'", "''");

        if (string.IsNullOrWhiteSpace(seriesCol))
        {
            return $@"
SELECT 
    date_trunc('{dateTruncPart}', parsed_date) AS x,
    {aggFunction}(parsed_value) AS y
FROM (
    SELECT 
        COALESCE(
            TRY_CAST(""{xCol}"" AS TIMESTAMP),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y-%m-%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%m/%d/%Y')
        ) AS parsed_date,
        CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE) AS parsed_value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL{filterClause}
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL
GROUP BY 1
ORDER BY 1;";
        }

        return $@"
SELECT 
    date_trunc('{dateTruncPart}', parsed_date) AS x,
    series,
    {aggFunction}(parsed_value) AS y
FROM (
    SELECT 
        COALESCE(
            TRY_CAST(""{xCol}"" AS TIMESTAMP),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y-%m-%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%m/%d/%Y')
        ) AS parsed_date,
        CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE) AS parsed_value,
        CAST(""{seriesCol}"" AS VARCHAR) AS series
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL AND ""{seriesCol}"" IS NOT NULL{filterClause}
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL AND series IS NOT NULL
GROUP BY 1, 2
ORDER BY 1, 2;";
    }

    public static string BuildMultiMetricTimeSeriesSql(string csvPath, ChartRecommendation recommendation, string filterClause)
    {
        var xCol = recommendation.Query.X.Column;
        var bin = recommendation.Query.X.Bin!.Value;
        var escapedPath = csvPath.Replace("'", "''");
        var dateTruncPart = MapTimeBin(bin);

        var metrics = recommendation.Query.YMetrics.Count > 0
            ? recommendation.Query.YMetrics
            : [recommendation.Query.Y];

        var parts = metrics.Select(metric =>
        {
            var agg = MapAggregation(metric.Aggregation ?? Aggregation.Avg);
            return $@"
SELECT
    date_trunc('{dateTruncPart}', parsed_date) AS x,
    '{metric.Column}' AS series,
    {agg}(parsed_value) AS y
FROM (
    SELECT
        COALESCE(
            TRY_CAST(""{xCol}"" AS TIMESTAMP),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%Y-%m-%d'),
            TRY_STRPTIME(CAST(""{xCol}"" AS VARCHAR), '%m/%d/%Y')
        ) AS parsed_date,
        CAST(REPLACE(CAST(""{metric.Column}"" AS VARCHAR), ',', '') AS DOUBLE) AS parsed_value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL AND ""{metric.Column}"" IS NOT NULL{filterClause}
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL
GROUP BY 1";
        });

        return string.Join("\nUNION ALL\n", parts) + "\nORDER BY 1, 2;";
    }

    public static string BuildBarSql(string csvPath, ChartRecommendation recommendation, string filterClause, int topN)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var agg = recommendation.Query.Y.Aggregation!.Value;
        var seriesCol = recommendation.Query.Series?.Column;
        var aggFunction = MapAggregation(agg);
        var escapedPath = csvPath.Replace("'", "''");

        if (string.IsNullOrWhiteSpace(seriesCol))
        {
            return $@"
SELECT 
    CAST(""{xCol}"" AS VARCHAR) AS category,
    {aggFunction}(CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS value
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL{filterClause}
GROUP BY 1
ORDER BY 2 DESC
LIMIT {topN};";
        }

        var groupedLimit = topN * 5;
        return $@"
SELECT 
    CAST(""{xCol}"" AS VARCHAR) AS category,
    CAST(""{seriesCol}"" AS VARCHAR) AS series,
    {aggFunction}(CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS value
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL AND ""{seriesCol}"" IS NOT NULL{filterClause}
GROUP BY 1, 2
ORDER BY 3 DESC
LIMIT {groupedLimit};";
    }

    public static string BuildParetoSql(string csvPath, ChartRecommendation recommendation, string filterClause, int topN)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var escapedPath = csvPath.Replace("'", "''");

        return $@"
WITH base AS (
    SELECT 
        CAST(""{xCol}"" AS VARCHAR) AS category,
        SUM(CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS contribution
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL{filterClause}
    GROUP BY 1
), ranked AS (
    SELECT category, contribution
    FROM base
    ORDER BY contribution DESC
    LIMIT {topN}
), cum AS (
    SELECT
        category,
        contribution,
        SUM(contribution) OVER (ORDER BY contribution DESC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)
            / NULLIF(SUM(contribution) OVER (), 0) * 100.0 AS cumulative_pct
    FROM ranked
)
SELECT category, contribution, cumulative_pct
FROM cum
ORDER BY contribution DESC;";
    }

    private static string MapTimeBin(TimeBin bin)
    {
        return bin switch
        {
            TimeBin.Day => "day",
            TimeBin.Week => "week",
            TimeBin.Month => "month",
            TimeBin.Quarter => "quarter",
            TimeBin.Year => "year",
            _ => "day"
        };
    }

    private static string MapAggregation(Aggregation aggregation)
    {
        return aggregation switch
        {
            Aggregation.Sum => "SUM",
            Aggregation.Avg => "AVG",
            Aggregation.Count => "COUNT",
            Aggregation.Min => "MIN",
            Aggregation.Max => "MAX",
            _ => "AVG"
        };
    }
}
