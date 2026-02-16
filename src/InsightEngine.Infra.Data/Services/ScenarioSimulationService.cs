using DuckDB.NET.Data;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace InsightEngine.Infra.Data.Services;

public class ScenarioSimulationService : IScenarioSimulationService
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ScenarioSimulationSettings _settings;
    private readonly ILogger<ScenarioSimulationService> _logger;

    public ScenarioSimulationService(
        IFileStorageService fileStorageService,
        IOptions<ScenarioSimulationSettings> settings,
        ILogger<ScenarioSimulationService> logger)
    {
        _fileStorageService = fileStorageService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Result<ScenarioSimulationResponse>> SimulateAsync(
        Guid datasetId,
        DatasetProfile profile,
        ScenarioRequest request,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;

        var errors = new List<string>();
        if (!ValidateRequestShape(request, errors))
        {
            return Result.Failure<ScenarioSimulationResponse>(errors);
        }

        var csvPath = _fileStorageService.GetFullPath($"{datasetId}.csv");
        if (!File.Exists(csvPath))
        {
            return Result.Failure<ScenarioSimulationResponse>($"Dataset not found: {datasetId}");
        }

        var columnLookup = profile.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        if (!columnLookup.TryGetValue(request.TargetMetric, out var metricColumn))
        {
            return Result.Failure<ScenarioSimulationResponse>($"Invalid targetMetric '{request.TargetMetric}'.");
        }

        if (metricColumn.InferredType != Domain.Enums.InferredType.Number)
        {
            return Result.Failure<ScenarioSimulationResponse>($"Invalid targetMetric '{metricColumn.Name}': metric must be numeric.");
        }

        if (!columnLookup.TryGetValue(request.TargetDimension, out var dimensionColumn))
        {
            return Result.Failure<ScenarioSimulationResponse>($"Invalid targetDimension '{request.TargetDimension}'.");
        }

        var normalizedFilters = NormalizeFilters(request.Filters, columnLookup, errors);
        var normalizedOperations = NormalizeOperations(request.Operations, dimensionColumn.Name, columnLookup, errors);

        if (errors.Count > 0)
        {
            return Result.Failure<ScenarioSimulationResponse>(errors);
        }

        var normalizedRequest = new ScenarioRequest
        {
            TargetMetric = metricColumn.Name,
            TargetDimension = dimensionColumn.Name,
            Aggregation = request.Aggregation,
            Filters = normalizedFilters,
            Operations = normalizedOperations
        };

        var queryHash = QueryHashHelper.ComputeScenarioQueryHash(normalizedRequest, datasetId);

        var sql = BuildSimulationSql(csvPath, normalizedRequest);
        var sw = Stopwatch.StartNew();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            using var reader = command.ExecuteReader();

            var deltaSeries = new List<ScenarioDeltaPoint>();
            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var point = new ScenarioDeltaPoint
                {
                    Dimension = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Baseline = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1),
                    Simulated = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2),
                    Delta = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3),
                    DeltaPercent = reader.IsDBNull(4) ? null : reader.GetDouble(4)
                };

                deltaSeries.Add(point);
            }

            sw.Stop();

            var baselineSeries = deltaSeries
                .Select(p => new ScenarioSeriesPoint { Dimension = p.Dimension, Value = p.Baseline })
                .ToList();

            var simulatedSeries = deltaSeries
                .Select(p => new ScenarioSeriesPoint { Dimension = p.Dimension, Value = p.Simulated })
                .ToList();

            var deltaPercents = deltaSeries
                .Where(p => p.DeltaPercent.HasValue)
                .Select(p => p.DeltaPercent!.Value)
                .ToList();

            var summary = new ScenarioDeltaSummary
            {
                AverageDeltaPercent = deltaPercents.Count == 0 ? 0 : Math.Round(deltaPercents.Average(), 3),
                MaxDeltaPercent = deltaPercents.Count == 0 ? 0 : Math.Round(deltaPercents.Max(), 3),
                MinDeltaPercent = deltaPercents.Count == 0 ? 0 : Math.Round(deltaPercents.Min(), 3),
                ChangedPoints = deltaSeries.Count(p => Math.Abs(p.Delta) > 0.0000001)
            };

            return Result.Success(new ScenarioSimulationResponse
            {
                DatasetId = datasetId,
                TargetMetric = normalizedRequest.TargetMetric,
                TargetDimension = normalizedRequest.TargetDimension,
                QueryHash = queryHash,
                RowCountReturned = deltaSeries.Count,
                DuckDbMs = sw.ElapsedMilliseconds,
                GeneratedSql = sql,
                BaselineSeries = baselineSeries,
                SimulatedSeries = simulatedSeries,
                DeltaSeries = deltaSeries,
                DeltaSummary = summary
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Simulation failed for dataset {DatasetId}", datasetId);
            return Result.Failure<ScenarioSimulationResponse>($"Simulation failed: {ex.Message}");
        }
    }

    private bool ValidateRequestShape(ScenarioRequest request, List<string> errors)
    {
        if (request == null)
        {
            errors.Add("Invalid scenario request.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.TargetMetric))
        {
            errors.Add("Invalid targetMetric: value is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetDimension))
        {
            errors.Add("Invalid targetDimension: value is required.");
        }

        var maxOperations = _settings.MaxOperations > 0 ? _settings.MaxOperations : 3;
        if (request.Operations == null || request.Operations.Count == 0)
        {
            errors.Add("Invalid operations: at least one operation is required.");
        }
        else if (request.Operations.Count > maxOperations)
        {
            errors.Add($"Invalid operations: no more than {maxOperations} operations are allowed.");
        }

        var maxFilters = _settings.MaxFilters > 0 ? _settings.MaxFilters : 3;
        if (request.Filters != null && request.Filters.Count > maxFilters)
        {
            errors.Add($"Invalid filters: no more than {maxFilters} filters are allowed.");
        }

        return errors.Count == 0;
    }

    private List<ChartFilter> NormalizeFilters(
        List<ChartFilter> filters,
        Dictionary<string, ColumnProfile> columnLookup,
        List<string> errors)
    {
        var normalized = new List<ChartFilter>();
        if (filters == null || filters.Count == 0)
        {
            return normalized;
        }

        foreach (var filter in filters.Take(_settings.MaxFilters > 0 ? _settings.MaxFilters : 3))
        {
            if (!columnLookup.TryGetValue(filter.Column, out var column))
            {
                errors.Add($"Invalid filter column '{filter.Column}'.");
                continue;
            }

            if (filter.Values == null || filter.Values.Count == 0)
            {
                errors.Add($"Invalid filter '{filter.Column}': values are required.");
                continue;
            }

            if (filter.Operator == FilterOperator.Between)
            {
                if (filter.Values.Count != 2)
                {
                    errors.Add($"Invalid filter '{filter.Column}': between requires exactly 2 values.");
                    continue;
                }

                if (!double.TryParse(filter.Values[0], NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                    !double.TryParse(filter.Values[1], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    errors.Add($"Invalid filter '{filter.Column}': between values must be numeric.");
                    continue;
                }
            }

            normalized.Add(new ChartFilter
            {
                Column = column.Name,
                Operator = filter.Operator,
                Values = filter.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList()
            });
        }

        return normalized;
    }

    private List<ScenarioOperation> NormalizeOperations(
        List<ScenarioOperation> operations,
        string defaultDimensionColumn,
        Dictionary<string, ColumnProfile> columnLookup,
        List<string> errors)
    {
        var normalized = new List<ScenarioOperation>();

        foreach (var operation in operations.Take(_settings.MaxOperations > 0 ? _settings.MaxOperations : 3))
        {
            switch (operation.Type)
            {
                case ScenarioOperationType.MultiplyMetric:
                    if (!operation.Factor.HasValue || double.IsNaN(operation.Factor.Value) || double.IsInfinity(operation.Factor.Value))
                    {
                        errors.Add("Invalid operation MultiplyMetric: factor must be a finite number.");
                        continue;
                    }
                    normalized.Add(new ScenarioOperation
                    {
                        Type = operation.Type,
                        Factor = operation.Factor
                    });
                    break;

                case ScenarioOperationType.AddConstant:
                    if (!operation.Constant.HasValue || double.IsNaN(operation.Constant.Value) || double.IsInfinity(operation.Constant.Value))
                    {
                        errors.Add("Invalid operation AddConstant: constant must be a finite number.");
                        continue;
                    }
                    normalized.Add(new ScenarioOperation
                    {
                        Type = operation.Type,
                        Constant = operation.Constant
                    });
                    break;

                case ScenarioOperationType.Clamp:
                    if (!operation.Min.HasValue && !operation.Max.HasValue)
                    {
                        errors.Add("Invalid operation Clamp: min or max is required.");
                        continue;
                    }

                    if (operation.Min.HasValue && operation.Max.HasValue && operation.Min > operation.Max)
                    {
                        errors.Add("Invalid operation Clamp: min cannot be greater than max.");
                        continue;
                    }

                    normalized.Add(new ScenarioOperation
                    {
                        Type = operation.Type,
                        Min = operation.Min,
                        Max = operation.Max
                    });
                    break;

                case ScenarioOperationType.RemoveCategory:
                case ScenarioOperationType.FilterOut:
                    var requestedColumn = string.IsNullOrWhiteSpace(operation.Column)
                        ? defaultDimensionColumn
                        : operation.Column.Trim();

                    if (!columnLookup.TryGetValue(requestedColumn, out var resolvedColumn))
                    {
                        errors.Add($"Invalid operation {operation.Type}: column '{requestedColumn}' was not found.");
                        continue;
                    }

                    var values = operation.Values
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v.Trim())
                        .ToList();

                    if (values.Count == 0)
                    {
                        errors.Add($"Invalid operation {operation.Type}: at least one value is required.");
                        continue;
                    }

                    normalized.Add(new ScenarioOperation
                    {
                        Type = operation.Type,
                        Column = resolvedColumn.Name,
                        Values = values
                    });
                    break;

                default:
                    errors.Add($"Invalid operation type '{operation.Type}'.");
                    break;
            }
        }

        return normalized;
    }

    private string BuildSimulationSql(string csvPath, ScenarioRequest request)
    {
        var escapedPath = csvPath.Replace("'", "''");
        var maxRows = _settings.MaxRowsReturned > 0 ? _settings.MaxRowsReturned : 200;
        var aggregation = request.Aggregation ?? Domain.Enums.Aggregation.Sum;
        var aggFunction = MapAggregation(aggregation);

        var metricColumn = EscapeIdentifier(request.TargetMetric);
        var dimensionColumn = EscapeIdentifier(request.TargetDimension);

        var baseMetricExpression = "metric_value";
        var simulatedMetricExpression = baseMetricExpression;

        var baselinePredicates = BuildFilterExpressions(request.Filters);
        var scenarioPredicates = new List<string>(baselinePredicates);

        foreach (var operation in request.Operations)
        {
            switch (operation.Type)
            {
                case ScenarioOperationType.MultiplyMetric:
                    simulatedMetricExpression = $"({simulatedMetricExpression} * {ToInvariant(operation.Factor!.Value)})";
                    break;
                case ScenarioOperationType.AddConstant:
                    simulatedMetricExpression = $"({simulatedMetricExpression} + {ToInvariant(operation.Constant!.Value)})";
                    break;
                case ScenarioOperationType.Clamp:
                    var min = operation.Min.HasValue ? ToInvariant(operation.Min.Value) : "-1e308";
                    var max = operation.Max.HasValue ? ToInvariant(operation.Max.Value) : "1e308";
                    simulatedMetricExpression = $"LEAST(GREATEST({simulatedMetricExpression}, {min}), {max})";
                    break;
                case ScenarioOperationType.RemoveCategory:
                case ScenarioOperationType.FilterOut:
                    scenarioPredicates.Add(BuildNotInPredicate(operation.Column!, operation.Values));
                    break;
            }
        }

        var baselineWhere = baselinePredicates.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", baselinePredicates);
        var scenarioWhere = scenarioPredicates.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", scenarioPredicates);

        return $@"
WITH source AS (
    SELECT
        CAST(""{dimensionColumn}"" AS VARCHAR) AS dimension_value,
        TRY_CAST(REPLACE(CAST(""{metricColumn}"" AS VARCHAR), ',', '') AS DOUBLE) AS metric_value,
        *
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
),
baseline AS (
    SELECT
        dimension_value AS dimension,
        {aggFunction}({baseMetricExpression}) AS value
    FROM source
    WHERE dimension_value IS NOT NULL AND metric_value IS NOT NULL{baselineWhere}
    GROUP BY 1
),
simulated AS (
    SELECT
        dimension_value AS dimension,
        {aggFunction}({simulatedMetricExpression}) AS value
    FROM source
    WHERE dimension_value IS NOT NULL AND metric_value IS NOT NULL{scenarioWhere}
    GROUP BY 1
),
combined AS (
    SELECT
        COALESCE(b.dimension, s.dimension) AS dimension,
        COALESCE(b.value, 0) AS baseline,
        COALESCE(s.value, 0) AS simulated
    FROM baseline b
    FULL OUTER JOIN simulated s ON s.dimension = b.dimension
)
SELECT
    dimension,
    baseline,
    simulated,
    (simulated - baseline) AS delta,
    CASE
        WHEN ABS(baseline) < 0.0000001 THEN NULL
        ELSE ((simulated - baseline) / ABS(baseline)) * 100
    END AS delta_percent
FROM combined
ORDER BY dimension
LIMIT {maxRows};
";
    }

    private static string MapAggregation(Domain.Enums.Aggregation aggregation)
    {
        return aggregation switch
        {
            Domain.Enums.Aggregation.Sum => "SUM",
            Domain.Enums.Aggregation.Avg => "AVG",
            Domain.Enums.Aggregation.Count => "COUNT",
            Domain.Enums.Aggregation.Min => "MIN",
            Domain.Enums.Aggregation.Max => "MAX",
            _ => "SUM"
        };
    }

    private static List<string> BuildFilterExpressions(IReadOnlyCollection<ChartFilter> filters)
    {
        var expressions = new List<string>();
        foreach (var filter in filters)
        {
            if (filter.Values == null || filter.Values.Count == 0)
            {
                continue;
            }

            var escapedColumn = EscapeIdentifier(filter.Column);
            var escapedValues = filter.Values.Select(EscapeSqlLiteral).ToList();

            var expression = filter.Operator switch
            {
                FilterOperator.Eq => $"CAST(\"{escapedColumn}\" AS VARCHAR) = '{escapedValues[0]}'",
                FilterOperator.NotEq => $"CAST(\"{escapedColumn}\" AS VARCHAR) <> '{escapedValues[0]}'",
                FilterOperator.Contains => $"CAST(\"{escapedColumn}\" AS VARCHAR) ILIKE '%{escapedValues[0]}%'",
                FilterOperator.In => $"CAST(\"{escapedColumn}\" AS VARCHAR) IN ({string.Join(", ", escapedValues.Select(v => $"'{v}'"))})",
                FilterOperator.Between => $"TRY_CAST(REPLACE(CAST(\"{escapedColumn}\" AS VARCHAR), ',', '') AS DOUBLE) BETWEEN {ToInvariant(filter.Values[0])} AND {ToInvariant(filter.Values[1])}",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(expression))
            {
                expressions.Add(expression);
            }
        }

        return expressions;
    }

    private static string BuildNotInPredicate(string column, IReadOnlyCollection<string> values)
    {
        var escapedColumn = EscapeIdentifier(column);
        var escapedValues = values.Select(v => $"'{EscapeSqlLiteral(v)}'");
        return $"CAST(\"{escapedColumn}\" AS VARCHAR) NOT IN ({string.Join(", ", escapedValues)})";
    }

    private static string EscapeIdentifier(string identifier)
    {
        return identifier.Replace("\"", "\"\"");
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static string ToInvariant(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string ToInvariant(string value)
    {
        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric))
        {
            throw new InvalidOperationException($"Invalid numeric literal '{value}'.");
        }

        return ToInvariant(numeric);
    }
}
