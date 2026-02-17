using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using DuckDB.NET.Data;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.MetadataIndex;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Infra.Data.Services;

public class DuckDbMetadataAnalyzer : IDuckDbMetadataAnalyzer
{
    private const double TypeInferenceThreshold = 0.9;
    private const int MaxPatternSamples = 500;

    private static readonly HashSet<string> BooleanLiterals =
    [
        "true", "false", "yes", "no", "1", "0", "t", "f", "y", "n", "sim", "nao", "não"
    ];

    private static readonly Regex UuidRegex = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NumericRegex = new(
        "^[+-]?[0-9]+([.,][0-9]+)?$",
        RegexOptions.Compiled);

    private static readonly Regex AlphaNumericCodeRegex = new(
        "^[A-Za-z]{1,6}[0-9]{1,8}$",
        RegexOptions.Compiled);

    private readonly ILogger<DuckDbMetadataAnalyzer> _logger;

    public DuckDbMetadataAnalyzer(ILogger<DuckDbMetadataAnalyzer> logger)
    {
        _logger = logger;
    }

    public async Task<List<ColumnIndex>> ComputeColumnProfilesAsync(
        string csvPath,
        int maxColumns = 200,
        int topValuesLimit = 20,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default)
    {
        var columns = await ReadCsvHeadersAsync(csvPath, cancellationToken);
        if (columns.Count == 0)
        {
            return [];
        }

        var limitedColumns = columns
            .Take(Math.Clamp(maxColumns, 1, 500))
            .ToList();

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var profileColumns = new List<ColumnIndex>(limitedColumns.Count);

        using var connection = CreateConnection();
        connection.Open();

        foreach (var column in limitedColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var escapedColumn = EscapeIdentifier(column);
            var columnExpr = $"src.{escapedColumn}";
            var dateExpr = BuildParsedDateExpression(columnExpr);
            var isNullExpr = $"({columnExpr} IS NULL OR TRIM(CAST({columnExpr} AS VARCHAR)) = '')";
            var boolList = string.Join(", ", BooleanLiterals.Select(literal => $"'{literal}'"));

            var profileSql = $@"
SELECT
    COUNT(*) AS total_count,
    SUM(CASE WHEN {isNullExpr} THEN 1 ELSE 0 END) AS null_count,
    APPROX_COUNT_DISTINCT(CASE WHEN NOT {isNullExpr} THEN CAST({columnExpr} AS VARCHAR) ELSE NULL END) AS distinct_count,
    SUM(CASE WHEN TRY_CAST(REPLACE(CAST({columnExpr} AS VARCHAR), ',', '') AS DOUBLE) IS NOT NULL THEN 1 ELSE 0 END) AS numeric_count,
    SUM(CASE WHEN {dateExpr} IS NOT NULL THEN 1 ELSE 0 END) AS date_count,
    SUM(CASE WHEN LOWER(TRIM(CAST({columnExpr} AS VARCHAR))) IN ({boolList}) THEN 1 ELSE 0 END) AS bool_count
FROM {sourceQuery} AS src;
";

            long totalCount;
            long nullCount;
            long distinctCount;
            long numericCount;
            long dateCount;
            long boolCount;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = profileSql;
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    continue;
                }

                totalCount = reader.IsDBNull(0) ? 0 : ReadLong(reader.GetValue(0));
                nullCount = reader.IsDBNull(1) ? 0 : ReadLong(reader.GetValue(1));
                distinctCount = reader.IsDBNull(2) ? 0 : ReadLong(reader.GetValue(2));
                numericCount = reader.IsDBNull(3) ? 0 : ReadLong(reader.GetValue(3));
                dateCount = reader.IsDBNull(4) ? 0 : ReadLong(reader.GetValue(4));
                boolCount = reader.IsDBNull(5) ? 0 : ReadLong(reader.GetValue(5));
            }

            var nonNullCount = Math.Max(totalCount - nullCount, 0);
            var inferredType = InferType(totalCount, nonNullCount, distinctCount, numericCount, dateCount, boolCount);

            var topValuesSql = $@"
SELECT CAST({columnExpr} AS VARCHAR) AS value
FROM {sourceQuery} AS src
WHERE NOT {isNullExpr}
GROUP BY 1
ORDER BY COUNT(*) DESC, value ASC
LIMIT {Math.Clamp(topValuesLimit, 1, 100)};
";

            var topValues = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = topValuesSql;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    topValues.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
                }
            }

            profileColumns.Add(new ColumnIndex
            {
                Name = column,
                InferredType = inferredType,
                NullRate = totalCount == 0 ? 0 : nullCount / (double)totalCount,
                DistinctCount = distinctCount,
                TopValues = topValues
            });
        }

        return profileColumns;
    }

    public async Task<NumericStatsIndex?> ComputeNumericStatsAsync(
        string csvPath,
        string column,
        int sampleRows = 50000,
        bool includeDistributions = true,
        int histogramBins = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureColumnExistsAsync(csvPath, column, cancellationToken);

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var escapedColumn = EscapeIdentifier(column);
        var valueExpr = $"TRY_CAST(REPLACE(CAST(src.{escapedColumn} AS VARCHAR), ',', '') AS DOUBLE)";

        var sql = $@"
WITH numeric_values AS (
    SELECT {valueExpr} AS numeric_value
    FROM {sourceQuery} AS src
)
SELECT
    COUNT(*) FILTER (WHERE numeric_value IS NOT NULL) AS value_count,
    MIN(numeric_value) AS min_value,
    MAX(numeric_value) AS max_value,
    AVG(numeric_value) AS mean_value,
    STDDEV_POP(numeric_value) AS stddev_value,
    QUANTILE_CONT(numeric_value, 0.05) AS p5,
    QUANTILE_CONT(numeric_value, 0.10) AS p10,
    QUANTILE_CONT(numeric_value, 0.50) AS p50,
    QUANTILE_CONT(numeric_value, 0.90) AS p90,
    QUANTILE_CONT(numeric_value, 0.95) AS p95
FROM numeric_values;
";

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var valueCount = reader.IsDBNull(0) ? 0L : ReadLong(reader.GetValue(0));
        if (valueCount == 0)
        {
            return null;
        }

        var stats = new NumericStatsIndex
        {
            Min = reader.IsDBNull(1) ? null : Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture),
            Max = reader.IsDBNull(2) ? null : Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
            Mean = reader.IsDBNull(3) ? null : Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture),
            StdDev = reader.IsDBNull(4) ? null : Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture),
            P5 = reader.IsDBNull(5) ? null : Convert.ToDouble(reader.GetValue(5), CultureInfo.InvariantCulture),
            P10 = reader.IsDBNull(6) ? null : Convert.ToDouble(reader.GetValue(6), CultureInfo.InvariantCulture),
            P50 = reader.IsDBNull(7) ? null : Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture),
            P90 = reader.IsDBNull(8) ? null : Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture),
            P95 = reader.IsDBNull(9) ? null : Convert.ToDouble(reader.GetValue(9), CultureInfo.InvariantCulture)
        };

        if (includeDistributions)
        {
            stats.Histogram = await ComputeHistogramAsync(
                csvPath,
                column,
                histogramBins,
                sampleRows,
                cancellationToken);
        }

        return stats;
    }

    public async Task<List<HistogramBinIndex>> ComputeHistogramAsync(
        string csvPath,
        string column,
        int bins = 20,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default)
    {
        await EnsureColumnExistsAsync(csvPath, column, cancellationToken);

        var boundedBins = Math.Clamp(bins, 4, 100);
        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var escapedColumn = EscapeIdentifier(column);
        var valueExpr = $"TRY_CAST(REPLACE(CAST(src.{escapedColumn} AS VARCHAR), ',', '') AS DOUBLE)";

        var sql = $@"
WITH numeric_values AS (
    SELECT {valueExpr} AS numeric_value
    FROM {sourceQuery} AS src
),
normalized AS (
    SELECT numeric_value
    FROM numeric_values
    WHERE numeric_value IS NOT NULL
),
stats AS (
    SELECT MIN(numeric_value) AS min_value, MAX(numeric_value) AS max_value
    FROM normalized
),
bucketed AS (
    SELECT
        CASE
            WHEN stats.max_value = stats.min_value THEN 0
            ELSE LEAST({boundedBins - 1}, GREATEST(0, CAST(FLOOR((normalized.numeric_value - stats.min_value) / NULLIF((stats.max_value - stats.min_value) / {boundedBins}, 0)) AS INTEGER)))
        END AS bucket_index,
        normalized.numeric_value
    FROM normalized
    CROSS JOIN stats
)
SELECT
    bucket_index,
    MIN(numeric_value) AS lower_bound,
    MAX(numeric_value) AS upper_bound,
    COUNT(*) AS frequency
FROM bucketed
GROUP BY bucket_index
ORDER BY bucket_index;
";

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var binsResult = new List<HistogramBinIndex>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.IsDBNull(1) || reader.IsDBNull(2))
            {
                continue;
            }

            binsResult.Add(new HistogramBinIndex
            {
                LowerBound = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture),
                UpperBound = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
                Count = reader.IsDBNull(3) ? 0 : ReadLong(reader.GetValue(3))
            });
        }

        return binsResult;
    }

    public async Task<DateStatsIndex?> ComputeDateStatsAsync(
        string csvPath,
        string column,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default)
    {
        await EnsureColumnExistsAsync(csvPath, column, cancellationToken);

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var escapedColumn = EscapeIdentifier(column);
        var dateExpr = BuildParsedDateExpression($"src.{escapedColumn}");

        using var connection = CreateConnection();
        connection.Open();

        var statsSql = $@"
WITH parsed_dates AS (
    SELECT {dateExpr} AS ts_value
    FROM {sourceQuery} AS src
)
SELECT
    COUNT(*) FILTER (WHERE ts_value IS NOT NULL) AS value_count,
    MIN(ts_value) AS min_value,
    MAX(ts_value) AS max_value
FROM parsed_dates;
";

        DateTime? minDate = null;
        DateTime? maxDate = null;
        long valueCount;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = statsSql;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            valueCount = reader.IsDBNull(0) ? 0 : ReadLong(reader.GetValue(0));
            if (valueCount == 0)
            {
                return null;
            }

            minDate = reader.IsDBNull(1) ? null : ReadDateTime(reader.GetValue(1));
            maxDate = reader.IsDBNull(2) ? null : ReadDateTime(reader.GetValue(2));
        }

        var coverage = new List<DateDensityBinIndex>();
        var coverageSql = $@"
WITH parsed_dates AS (
    SELECT {dateExpr} AS ts_value
    FROM {sourceQuery} AS src
)
SELECT
    DATE_TRUNC('month', ts_value) AS bucket_start,
    DATE_TRUNC('month', ts_value) + INTERVAL '1 month' AS bucket_end,
    COUNT(*) AS frequency
FROM parsed_dates
WHERE ts_value IS NOT NULL
GROUP BY 1, 2
ORDER BY 1
LIMIT 240;
";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = coverageSql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                {
                    continue;
                }

                coverage.Add(new DateDensityBinIndex
                {
                    Start = ReadDateTime(reader.GetValue(0)),
                    End = ReadDateTime(reader.GetValue(1)),
                    Count = reader.IsDBNull(2) ? 0 : ReadLong(reader.GetValue(2))
                });
            }
        }

        var gaps = new List<DateGapHintIndex>();
        var gapsSql = $@"
WITH parsed_dates AS (
    SELECT DATE_TRUNC('day', {dateExpr}) AS day_value
    FROM {sourceQuery} AS src
),
ordered_dates AS (
    SELECT DISTINCT day_value
    FROM parsed_dates
    WHERE day_value IS NOT NULL
),
with_prev AS (
    SELECT
        day_value,
        LAG(day_value) OVER (ORDER BY day_value) AS prev_day
    FROM ordered_dates
)
SELECT
    prev_day,
    day_value,
    DATE_DIFF('day', prev_day, day_value) - 1 AS missing_days
FROM with_prev
WHERE prev_day IS NOT NULL AND DATE_DIFF('day', prev_day, day_value) > 1
ORDER BY missing_days DESC, prev_day
LIMIT 20;
";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = gapsSql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                {
                    continue;
                }

                var previousDate = ReadDateTime(reader.GetValue(0));
                var currentDate = ReadDateTime(reader.GetValue(1));
                var missingDays = reader.IsDBNull(2) ? 0 : ReadLong(reader.GetValue(2));

                gaps.Add(new DateGapHintIndex
                {
                    GapStart = previousDate.AddDays(1),
                    GapEnd = currentDate.AddDays(-1),
                    ApproxMissingPeriods = Math.Max(0, missingDays)
                });
            }
        }

        return new DateStatsIndex
        {
            Min = minDate,
            Max = maxDate,
            Coverage = coverage,
            Gaps = gaps
        };
    }

    public async Task<StringStatsIndex?> ComputeStringStatsAsync(
        string csvPath,
        string column,
        bool includeStringPatterns = true,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default)
    {
        await EnsureColumnExistsAsync(csvPath, column, cancellationToken);

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var escapedColumn = EscapeIdentifier(column);
        var valueExpr = $"TRIM(CAST(src.{escapedColumn} AS VARCHAR))";
        var isNullExpr = $"(src.{escapedColumn} IS NULL OR {valueExpr} = '')";

        var statsSql = $@"
SELECT
    COUNT(*) FILTER (WHERE NOT {isNullExpr}) AS value_count,
    AVG(LENGTH({valueExpr})) FILTER (WHERE NOT {isNullExpr}) AS avg_len,
    MIN(LENGTH({valueExpr})) FILTER (WHERE NOT {isNullExpr}) AS min_len,
    MAX(LENGTH({valueExpr})) FILTER (WHERE NOT {isNullExpr}) AS max_len
FROM {sourceQuery} AS src;
";

        using var connection = CreateConnection();
        connection.Open();

        double avgLength;
        int minLength;
        int maxLength;
        long valueCount;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = statsSql;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            valueCount = reader.IsDBNull(0) ? 0 : ReadLong(reader.GetValue(0));
            if (valueCount == 0)
            {
                return null;
            }

            avgLength = reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            minLength = reader.IsDBNull(2) ? 0 : ReadInt(reader.GetValue(2));
            maxLength = reader.IsDBNull(3) ? 0 : ReadInt(reader.GetValue(3));
        }

        var result = new StringStatsIndex
        {
            AvgLength = avgLength,
            MinLength = minLength,
            MaxLength = maxLength
        };

        if (!includeStringPatterns)
        {
            return result;
        }

        var valuesSql = $@"
SELECT {valueExpr} AS value
FROM {sourceQuery} AS src
WHERE NOT {isNullExpr}
LIMIT {MaxPatternSamples};
";

        var patternCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = valuesSql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var value = reader.GetValue(0)?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var hint = DetectPatternHint(value);
                patternCounts[hint] = patternCounts.GetValueOrDefault(hint) + 1;
            }
        }

        result.PatternHints = patternCounts
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Key)
            .Take(5)
            .ToList();

        return result;
    }

    public async Task<List<KeyCandidate>> ComputeCandidateKeysAsync(
        string csvPath,
        IReadOnlyCollection<ColumnIndex> columns,
        int sampleRows = 50000,
        int maxSingleColumnCandidates = 10,
        int maxCompositeCandidates = 10,
        CancellationToken cancellationToken = default)
    {
        if (columns.Count == 0)
        {
            return [];
        }

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var candidates = new List<KeyCandidate>();

        using var connection = CreateConnection();
        connection.Open();

        foreach (var column in columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureColumnExistsAsync(csvPath, column.Name, cancellationToken);

            var escapedColumn = EscapeIdentifier(column.Name);
            var valueExpr = $"TRIM(CAST(src.{escapedColumn} AS VARCHAR))";
            var isNullExpr = $"(src.{escapedColumn} IS NULL OR {valueExpr} = '')";

            var sql = $@"
SELECT
    COUNT(*) AS total_count,
    SUM(CASE WHEN {isNullExpr} THEN 1 ELSE 0 END) AS null_count,
    APPROX_COUNT_DISTINCT(CASE WHEN NOT {isNullExpr} THEN {valueExpr} ELSE NULL END) AS distinct_non_null
FROM {sourceQuery} AS src;
";

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();

            if (!reader.Read())
            {
                continue;
            }

            var total = reader.IsDBNull(0) ? 0 : ReadLong(reader.GetValue(0));
            if (total == 0)
            {
                continue;
            }

            var nullCount = reader.IsDBNull(1) ? 0 : ReadLong(reader.GetValue(1));
            var distinct = reader.IsDBNull(2) ? 0 : ReadLong(reader.GetValue(2));
            var uniqueness = distinct / (double)total;
            var nullRate = nullCount / (double)total;

            if (uniqueness < 0.6)
            {
                continue;
            }

            candidates.Add(new KeyCandidate
            {
                Columns = [column.Name],
                UniquenessRatio = uniqueness,
                NullRate = nullRate,
                Confidence = ComputeConfidence(uniqueness, nullRate)
            });
        }

        var singleCandidates = candidates
            .OrderByDescending(c => c.UniquenessRatio)
            .ThenBy(c => c.NullRate)
            .Take(Math.Clamp(maxSingleColumnCandidates, 1, 30))
            .ToList();

        var seedColumns = columns
            .OrderByDescending(c => c.DistinctCount)
            .ThenBy(c => c.NullRate)
            .Take(8)
            .Select(c => c.Name)
            .ToList();

        var compositeCandidates = new List<KeyCandidate>();

        for (var leftIndex = 0; leftIndex < seedColumns.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < seedColumns.Count; rightIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var left = seedColumns[leftIndex];
                var right = seedColumns[rightIndex];

                var leftExpr = $"TRIM(CAST(src.{EscapeIdentifier(left)} AS VARCHAR))";
                var rightExpr = $"TRIM(CAST(src.{EscapeIdentifier(right)} AS VARCHAR))";
                var pairNullExpr = $"(src.{EscapeIdentifier(left)} IS NULL OR {leftExpr} = '' OR src.{EscapeIdentifier(right)} IS NULL OR {rightExpr} = '')";

                var sql = $@"
SELECT
    COUNT(*) AS total_count,
    SUM(CASE WHEN {pairNullExpr} THEN 1 ELSE 0 END) AS null_count,
    APPROX_COUNT_DISTINCT(CASE WHEN NOT {pairNullExpr} THEN CONCAT({leftExpr}, '||', {rightExpr}) ELSE NULL END) AS distinct_pairs
FROM {sourceQuery} AS src;
";

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                using var reader = command.ExecuteReader();

                if (!reader.Read())
                {
                    continue;
                }

                var total = reader.IsDBNull(0) ? 0 : ReadLong(reader.GetValue(0));
                if (total == 0)
                {
                    continue;
                }

                var nullCount = reader.IsDBNull(1) ? 0 : ReadLong(reader.GetValue(1));
                var distinctPairs = reader.IsDBNull(2) ? 0 : ReadLong(reader.GetValue(2));
                var uniqueness = distinctPairs / (double)total;
                var nullRate = nullCount / (double)total;

                if (uniqueness < 0.75)
                {
                    continue;
                }

                compositeCandidates.Add(new KeyCandidate
                {
                    Columns = [left, right],
                    UniquenessRatio = uniqueness,
                    NullRate = nullRate,
                    Confidence = ComputeConfidence(uniqueness, nullRate)
                });
            }
        }

        compositeCandidates = compositeCandidates
            .OrderByDescending(c => c.UniquenessRatio)
            .ThenBy(c => c.NullRate)
            .Take(Math.Clamp(maxCompositeCandidates, 1, 30))
            .ToList();

        return [.. singleCandidates, .. compositeCandidates];
    }

    public async Task<CorrelationIndex> ComputeNumericCorrelationsAsync(
        string csvPath,
        IReadOnlyCollection<ColumnIndex> columns,
        int limitColumns = 50,
        int topK = 10,
        int sampleRows = 50000,
        CancellationToken cancellationToken = default)
    {
        var numericColumns = columns
            .Where(c => c.InferredType == InferredType.Number)
            .OrderByDescending(c => c.DistinctCount)
            .ThenBy(c => c.NullRate)
            .Select(c => c.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limitColumns, 2, 60))
            .ToList();

        if (numericColumns.Count < 2)
        {
            return new CorrelationIndex
            {
                CandidateColumnCount = numericColumns.Count,
                Edges = []
            };
        }

        foreach (var column in numericColumns)
        {
            await EnsureColumnExistsAsync(csvPath, column, cancellationToken);
        }

        var sourceQuery = BuildSampledSourceQuery(csvPath, sampleRows);
        var normalizedColumns = string.Join(",\n        ",
            numericColumns.Select(column =>
                $"TRY_CAST(REPLACE(CAST(src.{EscapeIdentifier(column)} AS VARCHAR), ',', '') AS DOUBLE) AS {EscapeIdentifier(column)}"));

        var edges = new List<CorrelationEdge>();
        using var connection = CreateConnection();
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $@"
CREATE OR REPLACE TEMP VIEW sampled_numeric AS
SELECT
    {normalizedColumns}
FROM {sourceQuery} AS src;
";
            command.ExecuteNonQuery();
        }

        var spearmanSupported = true;

        for (var i = 0; i < numericColumns.Count; i++)
        {
            for (var j = i + 1; j < numericColumns.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var left = numericColumns[i];
                var right = numericColumns[j];

                var pearsonSql = $@"
SELECT
    CORR({EscapeIdentifier(left)}, {EscapeIdentifier(right)}) AS score,
    COUNT(*) AS sample_size
FROM sampled_numeric
WHERE {EscapeIdentifier(left)} IS NOT NULL AND {EscapeIdentifier(right)} IS NOT NULL;
";

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = pearsonSql;
                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        var sampleSize = reader.IsDBNull(1) ? 0 : ReadLong(reader.GetValue(1));
                        if (!reader.IsDBNull(0) && sampleSize > 2)
                        {
                            var score = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
                            edges.Add(BuildCorrelationEdge(left, right, CorrelationMethod.Pearson, score, sampleSize));
                        }
                    }
                }

                if (!spearmanSupported)
                {
                    continue;
                }

                var spearmanSql = $@"
WITH base AS (
    SELECT {EscapeIdentifier(left)} AS x, {EscapeIdentifier(right)} AS y
    FROM sampled_numeric
    WHERE {EscapeIdentifier(left)} IS NOT NULL AND {EscapeIdentifier(right)} IS NOT NULL
),
ranked AS (
    SELECT
        CAST(DENSE_RANK() OVER (ORDER BY x) AS DOUBLE) AS rx,
        CAST(DENSE_RANK() OVER (ORDER BY y) AS DOUBLE) AS ry
    FROM base
)
SELECT
    CORR(rx, ry) AS score,
    COUNT(*) AS sample_size
FROM ranked;
";

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = spearmanSql;
                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        var sampleSize = reader.IsDBNull(1) ? 0 : ReadLong(reader.GetValue(1));
                        if (!reader.IsDBNull(0) && sampleSize > 2)
                        {
                            var score = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
                            edges.Add(BuildCorrelationEdge(left, right, CorrelationMethod.Spearman, score, sampleSize));
                        }
                    }
                }
                catch (Exception ex)
                {
                    spearmanSupported = false;
                    _logger.LogWarning(ex, "Spearman correlation is unavailable in current DuckDB context. Falling back to Pearson only.");
                }
            }
        }

        var selectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedEdges = new List<CorrelationEdge>();

        foreach (var column in numericColumns)
        {
            var perColumnTopEdges = edges
                .Where(edge => edge.LeftColumn.Equals(column, StringComparison.OrdinalIgnoreCase) || edge.RightColumn.Equals(column, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(edge => Math.Abs(edge.Score))
                .Take(Math.Clamp(topK, 1, 30));

            foreach (var edge in perColumnTopEdges)
            {
                var key = BuildEdgeKey(edge);
                if (!selectedKeys.Add(key))
                {
                    continue;
                }

                selectedEdges.Add(edge);
            }
        }

        selectedEdges = selectedEdges
            .OrderByDescending(edge => Math.Abs(edge.Score))
            .ThenBy(edge => edge.LeftColumn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.RightColumn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Method)
            .ToList();

        return new CorrelationIndex
        {
            CandidateColumnCount = numericColumns.Count,
            Edges = selectedEdges
        };
    }

    private static CorrelationEdge BuildCorrelationEdge(
        string left,
        string right,
        CorrelationMethod method,
        double score,
        long sampleSize)
    {
        var abs = Math.Abs(score);
        var strength = abs switch
        {
            >= 0.7 => CorrelationStrength.High,
            >= 0.3 => CorrelationStrength.Medium,
            _ => CorrelationStrength.Low
        };

        var direction = method is CorrelationMethod.Pearson or CorrelationMethod.Spearman
            ? score switch
            {
                > 0 => CorrelationDirection.Positive,
                < 0 => CorrelationDirection.Negative,
                _ => CorrelationDirection.None
            }
            : CorrelationDirection.None;

        var confidence = sampleSize switch
        {
            >= 1000 => ConfidenceLevel.High,
            >= 100 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };

        return new CorrelationEdge
        {
            LeftColumn = left,
            RightColumn = right,
            Method = method,
            Score = score,
            Strength = strength,
            Direction = direction,
            SampleSize = sampleSize,
            Confidence = confidence
        };
    }

    private static ConfidenceLevel ComputeConfidence(double uniquenessRatio, double nullRate)
    {
        if (uniquenessRatio >= 0.995 && nullRate <= 0.01)
        {
            return ConfidenceLevel.High;
        }

        if (uniquenessRatio >= 0.9 && nullRate <= 0.05)
        {
            return ConfidenceLevel.Medium;
        }

        return ConfidenceLevel.Low;
    }

    private static string BuildEdgeKey(CorrelationEdge edge)
    {
        var ordered = new[] { edge.LeftColumn, edge.RightColumn }
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return $"{ordered[0]}|{ordered[1]}|{edge.Method}";
    }

    private static InferredType InferType(
        long totalCount,
        long nonNullCount,
        long distinctCount,
        long numericCount,
        long dateCount,
        long boolCount)
    {
        if (nonNullCount == 0 || totalCount == 0)
        {
            return InferredType.String;
        }

        var boolRatio = boolCount / (double)nonNullCount;
        var dateRatio = dateCount / (double)nonNullCount;
        var numericRatio = numericCount / (double)nonNullCount;

        if (boolRatio >= TypeInferenceThreshold)
        {
            return InferredType.Boolean;
        }

        if (dateRatio >= TypeInferenceThreshold)
        {
            return InferredType.Date;
        }

        if (numericRatio >= TypeInferenceThreshold)
        {
            return InferredType.Number;
        }

        var categoryThreshold = Math.Max(20, totalCount * 0.05);
        if (distinctCount <= categoryThreshold)
        {
            return InferredType.Category;
        }

        return InferredType.String;
    }

    private static string DetectPatternHint(string value)
    {
        if (UuidRegex.IsMatch(value))
        {
            return "uuid";
        }

        if (EmailRegex.IsMatch(value))
        {
            return "email";
        }

        if (NumericRegex.IsMatch(value))
        {
            return "numeric-string";
        }

        if (AlphaNumericCodeRegex.IsMatch(value))
        {
            return "alphanumeric-code";
        }

        if (value.StartsWith("{") && value.EndsWith("}"))
        {
            return "json-like";
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "url";
        }

        if (value.Length > 120)
        {
            return "long-text";
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return "phrase";
        }

        return "token";
    }

    private static string BuildSampledSourceQuery(string csvPath, int sampleRows)
    {
        var escapedPath = csvPath.Replace("'", "''", StringComparison.Ordinal);
        var boundedSampleRows = Math.Clamp(sampleRows, 1000, 500000);

        return $@"(
    SELECT * EXCLUDE (__row_id)
    FROM (
        SELECT ROW_NUMBER() OVER () AS __row_id, *
        FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    ) sampled
    WHERE __row_id <= {boundedSampleRows}
)";
    }

    private static string BuildParsedDateExpression(string columnExpression)
    {
        return $@"COALESCE(
            TRY_CAST({columnExpression} AS TIMESTAMP),
            TRY_STRPTIME(CAST({columnExpression} AS VARCHAR), '%Y%m%d'),
            TRY_STRPTIME(CAST({columnExpression} AS VARCHAR), '%d/%m/%Y'),
            TRY_STRPTIME(CAST({columnExpression} AS VARCHAR), '%Y-%m-%d'),
            TRY_STRPTIME(CAST({columnExpression} AS VARCHAR), '%m/%d/%Y')
        )";
    }

    private static string EscapeIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static long ReadLong(object? value)
    {
        return value switch
        {
            null => 0,
            DBNull => 0,
            long typedLong => typedLong,
            int typedInt => typedInt,
            short typedShort => typedShort,
            byte typedByte => typedByte,
            BigInteger bigInteger => (long)bigInteger,
            decimal typedDecimal => (long)typedDecimal,
            double typedDouble => (long)typedDouble,
            float typedFloat => (long)typedFloat,
            string text when long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };
    }

    private static int ReadInt(object? value)
    {
        return (int)ReadLong(value);
    }

    private static DateTime ReadDateTime(object? value)
    {
        return value switch
        {
            null => throw new InvalidOperationException("Cannot read date/time from null value."),
            DBNull => throw new InvalidOperationException("Cannot read date/time from DBNull."),
            DateTime typedDateTime => typedDateTime,
            DateOnly typedDateOnly => typedDateOnly.ToDateTime(TimeOnly.MinValue),
            DateTimeOffset typedDateTimeOffset => typedDateTimeOffset.UtcDateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDateTime) => parsedDateTime,
            _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture)
        };
    }

    private static DuckDBConnection CreateConnection()
    {
        return new DuckDBConnection("DataSource=:memory:");
    }

    private async Task EnsureColumnExistsAsync(string csvPath, string column, CancellationToken cancellationToken)
    {
        var headers = await ReadCsvHeadersAsync(csvPath, cancellationToken);
        if (!headers.Any(header => header.Equals(column, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Column '{column}' does not exist in dataset schema.");
        }
    }

    private static async Task<List<string>> ReadCsvHeadersAsync(string csvPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("Dataset CSV file not found.", csvPath);
        }

        await using var stream = File.OpenRead(csvPath);
        using var streamReader = new StreamReader(stream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null
        };

        using var csv = new CsvReader(streamReader, config);

        if (!await csv.ReadAsync() || cancellationToken.IsCancellationRequested)
        {
            return [];
        }

        csv.ReadHeader();
        return csv.HeaderRecord?.ToList() ?? [];
    }
}
