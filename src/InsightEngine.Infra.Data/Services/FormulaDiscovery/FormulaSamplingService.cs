using System.Data;
using DuckDB.NET.Data;
using InsightEngine.Domain.Interfaces;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaSamplingService
{
    private readonly IFileStorageService _fileStorageService;

    public FormulaSamplingService(IFileStorageService fileStorageService)
    {
        _fileStorageService = fileStorageService;
    }

    public async Task<FormulaSampleSet> LoadSampleAsync(
        Guid datasetId,
        string targetColumn,
        IReadOnlyCollection<string> featureColumns,
        int sampleCap = 50_000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetColumn))
        {
            throw new ArgumentException("Target column is required.", nameof(targetColumn));
        }

        var cleanedFeatures = featureColumns
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Where(column => !string.Equals(column, targetColumn, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanedFeatures.Count == 0)
        {
            throw new ArgumentException("At least one feature column is required.", nameof(featureColumns));
        }

        var csvPath = _fileStorageService.GetFullPath($"{datasetId:D}.csv");
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("Dataset CSV file was not found.", csvPath);
        }

        var boundedSampleCap = Math.Clamp(sampleCap, 1_000, 100_000);
        var escapedCsvPath = EscapeSqlLiteral(csvPath);
        var sourceQuery = $"read_csv_auto('{escapedCsvPath}', HEADER=true, SAMPLE_SIZE=-1, ALL_VARCHAR=true)";

        using var connection = CreateConnection();
        connection.Open();

        var projectedColumns = new List<string>
        {
            $"{BuildNumericExpression($"src.{EscapeIdentifier(targetColumn)}")} AS y"
        };
        projectedColumns.AddRange(cleanedFeatures.Select((feature, index) =>
            $"{BuildNumericExpression($"src.{EscapeIdentifier(feature)}")} AS f{index}"));
        var projectionSql = string.Join(", ", projectedColumns);

        var countSql = $@"
WITH source AS (
    SELECT {projectionSql}
    FROM {sourceQuery} AS src
),
filtered AS (
    SELECT *
    FROM source
    WHERE y IS NOT NULL
)
SELECT COUNT(*)
FROM filtered;";

        int eligibleRows;
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = countSql;
            var value = countCmd.ExecuteScalar();
            eligibleRows = value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt32(value);
        }

        if (eligibleRows == 0)
        {
            return new FormulaSampleSet
            {
                TargetColumn = targetColumn,
                FeatureColumns = cleanedFeatures,
                X = Array.Empty<double[]>(),
                Y = Array.Empty<double>(),
                OriginalRowCount = 0,
                AcceptedRowCount = 0,
                DroppedRowCount = 0
            };
        }

        var needsSampling = eligibleRows > boundedSampleCap;
        var hashExpression = BuildDeterministicHashExpression(cleanedFeatures.Count);
        var dataSql = $@"
WITH source AS (
    SELECT {projectionSql}
    FROM {sourceQuery} AS src
),
filtered AS (
    SELECT *
    FROM source
    WHERE y IS NOT NULL
),
sampled AS (
    SELECT *
    FROM filtered
    {(needsSampling ? $"ORDER BY {hashExpression} LIMIT {boundedSampleCap}" : string.Empty)}
)
SELECT y, {string.Join(", ", Enumerable.Range(0, cleanedFeatures.Count).Select(index => $"f{index}"))}
FROM sampled;";

        var rawRows = new List<(double Y, double?[] Features)>();
        using (var dataCmd = connection.CreateCommand())
        {
            dataCmd.CommandText = dataSql;
            using var reader = dataCmd.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var targetValue = Convert.ToDouble(reader.GetValue(0));
                if (!double.IsFinite(targetValue))
                {
                    continue;
                }

                var features = new double?[cleanedFeatures.Count];
                for (var i = 0; i < cleanedFeatures.Count; i++)
                {
                    features[i] = ReadNullableDouble(reader, i + 1);
                }

                rawRows.Add((targetValue, features));
            }
        }

        if (rawRows.Count == 0)
        {
            return new FormulaSampleSet
            {
                TargetColumn = targetColumn,
                FeatureColumns = cleanedFeatures,
                X = Array.Empty<double[]>(),
                Y = Array.Empty<double>(),
                OriginalRowCount = eligibleRows,
                AcceptedRowCount = 0,
                DroppedRowCount = 0
            };
        }

        var maxMissingFeatures = Math.Max(1, (int)Math.Floor(cleanedFeatures.Count * 0.4d));
        var filteredRows = new List<(double Y, double?[] Features)>(rawRows.Count);
        var droppedRows = 0;

        foreach (var row in rawRows)
        {
            var missingCount = row.Features.Count(value => !value.HasValue);
            if (missingCount > maxMissingFeatures)
            {
                droppedRows++;
                continue;
            }

            filteredRows.Add(row);
        }

        if (filteredRows.Count == 0)
        {
            return new FormulaSampleSet
            {
                TargetColumn = targetColumn,
                FeatureColumns = cleanedFeatures,
                X = Array.Empty<double[]>(),
                Y = Array.Empty<double>(),
                OriginalRowCount = eligibleRows,
                AcceptedRowCount = 0,
                DroppedRowCount = droppedRows
            };
        }

        var featureMeans = new double[cleanedFeatures.Count];
        for (var i = 0; i < cleanedFeatures.Count; i++)
        {
            var observed = filteredRows
                .Select(row => row.Features[i])
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

            featureMeans[i] = observed.Count == 0 ? 0d : observed.Average();
        }

        var x = new double[filteredRows.Count][];
        var y = new double[filteredRows.Count];

        for (var rowIndex = 0; rowIndex < filteredRows.Count; rowIndex++)
        {
            var row = filteredRows[rowIndex];
            y[rowIndex] = row.Y;
            x[rowIndex] = new double[cleanedFeatures.Count];

            for (var colIndex = 0; colIndex < cleanedFeatures.Count; colIndex++)
            {
                x[rowIndex][colIndex] = row.Features[colIndex] ?? featureMeans[colIndex];
            }
        }

        return new FormulaSampleSet
        {
            TargetColumn = targetColumn,
            FeatureColumns = cleanedFeatures,
            X = x,
            Y = y,
            OriginalRowCount = eligibleRows,
            AcceptedRowCount = filteredRows.Count,
            DroppedRowCount = droppedRows
        };
    }

    private static string BuildDeterministicHashExpression(int featureCount)
    {
        var terms = new List<string> { "COALESCE(CAST(y AS VARCHAR), '')" };
        terms.AddRange(Enumerable.Range(0, featureCount).Select(index => $"COALESCE(CAST(f{index} AS VARCHAR), '')"));
        return $"hash(concat_ws('|', {string.Join(", ", terms)}))";
    }

    private static string BuildNumericExpression(string sourceExpression)
    {
        return $"TRY_CAST(REPLACE(CAST({sourceExpression} AS VARCHAR), ',', '') AS DOUBLE)";
    }

    private static double? ReadNullableDouble(IDataRecord reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return null;
        }

        var value = Convert.ToDouble(reader.GetValue(index));
        return double.IsFinite(value) ? value : null;
    }

    private static DuckDBConnection CreateConnection()
    {
        return new DuckDBConnection("DataSource=:memory:");
    }

    private static string EscapeIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
