using DuckDB.NET.Data;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Infra.Data.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace InsightEngine.Infra.Data.Services;

/// <summary>
/// Executor de gráficos usando DuckDB como motor analítico
/// </summary>
public class ChartExecutionService : IChartExecutionService
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<ChartExecutionService> _logger;
    private readonly ChartExecutionSettings _settings;

    public ChartExecutionService(
        IFileStorageService fileStorageService,
        ILogger<ChartExecutionService> logger,
        IOptions<ChartExecutionSettings> settings)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<Result<ChartExecutionResult>> ExecuteAsync(
        Guid datasetId,
        ChartRecommendation recommendation,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validar suporte de biblioteca
            if (recommendation.Chart.Library != ChartLibrary.ECharts)
                return Result.Failure<ChartExecutionResult>(
                    $"Unsupported chart library: {recommendation.Chart.Library}. Only ECharts is supported.");

            // 2. Resolver path do CSV
            var csvPath = _fileStorageService.GetFullPath($"{datasetId}.csv");
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("Dataset file not found: {DatasetId}", datasetId);
                return Result.Failure<ChartExecutionResult>($"Dataset file not found: {datasetId}");
            }

            // 3. Dispatcher por tipo de chart
            Result<ChartExecutionResult> result = recommendation.Chart.Type switch
            {
                ChartType.Line => await ExecuteLineAsync(csvPath, recommendation, ct),
                ChartType.Bar => await ExecuteBarAsync(csvPath, recommendation, ct),
                ChartType.Scatter => await ExecuteScatterAsync(csvPath, recommendation, ct),
                ChartType.Histogram => await ExecuteHistogramAsync(csvPath, recommendation, ct),
                _ => Result.Failure<ChartExecutionResult>(
                    $"Unsupported chart type: {recommendation.Chart.Type}. Supported: Line, Bar, Scatter, Histogram.")
            };

            sw.Stop();

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Chart executed successfully: Type={ChartType}, RecommendationId={RecommendationId}, Rows={RowCount}, TotalMs={TotalMs}",
                    recommendation.Chart.Type, recommendation.Id, result.Data!.RowCount, sw.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error executing chart: {RecommendationId}", recommendation.Id);
            return Result.Failure<ChartExecutionResult>($"Error executing chart: {ex.Message}");
        }
    }

    // ===========================
    // LINE CHART EXECUTION
    // ===========================

    private async Task<Result<ChartExecutionResult>> ExecuteLineAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        var swDuckDb = Stopwatch.StartNew();

        try
        {
            // Validar eixos
            if (recommendation.Query.X.Role != AxisRole.Time)
                return Result.Failure<ChartExecutionResult>("Line chart requires X axis with role Time.");

            if (!recommendation.Query.X.Bin.HasValue)
                return Result.Failure<ChartExecutionResult>("Line chart requires X axis with TimeBin defined.");

            if (recommendation.Query.Y.Role != AxisRole.Measure)
                return Result.Failure<ChartExecutionResult>("Line chart requires Y axis with role Measure.");

            if (!recommendation.Query.Y.Aggregation.HasValue)
                return Result.Failure<ChartExecutionResult>("Line chart requires Y axis with aggregation defined.");

            // Gerar e executar SQL
            var sql = BuildTimeSeriesSQL(csvPath, recommendation);
            var dataResult = await ExecuteQueryAsync(sql, ct);
            swDuckDb.Stop();

            if (!dataResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(dataResult.Errors);

            // Aplicar gap filling
            var processedPoints = ApplyGapFilling(dataResult.Data!, recommendation.Query.X.Bin!.Value);

            // Montar ECharts option
            var option = BuildEChartsOption(recommendation, processedPoints);

            return Result.Success(new ChartExecutionResult
            {
                Option = option,
                DuckDbMs = swDuckDb.ElapsedMilliseconds,
                GeneratedSql = sql,
                RowCount = processedPoints.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Line chart");
            return Result.Failure<ChartExecutionResult>($"Line chart execution failed: {ex.Message}");
        }
    }

    // ===========================
    // BAR CHART EXECUTION
    // ===========================

    private async Task<Result<ChartExecutionResult>> ExecuteBarAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        var swDuckDb = Stopwatch.StartNew();

        try
        {
            // Validar eixos: X=Category, Y=Measure
            if (recommendation.Query.X.Role != AxisRole.Category)
                return Result.Failure<ChartExecutionResult>("Bar chart requires X axis with role Category.");

            if (recommendation.Query.Y.Role != AxisRole.Measure)
                return Result.Failure<ChartExecutionResult>("Bar chart requires Y axis with role Measure.");

            if (!recommendation.Query.Y.Aggregation.HasValue)
                return Result.Failure<ChartExecutionResult>("Bar chart requires Y axis with aggregation defined.");

            // Gerar e executar SQL
            var sql = BuildBarSQL(csvPath, recommendation);
            var dataResult = await ExecuteBarQueryAsync(sql, ct);
            swDuckDb.Stop();

            if (!dataResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(dataResult.Errors);

            var categories = dataResult.Data!;

            // Montar ECharts option
            var option = BuildBarEChartsOption(recommendation, categories);

            return Result.Success(new ChartExecutionResult
            {
                Option = option,
                DuckDbMs = swDuckDb.ElapsedMilliseconds,
                GeneratedSql = sql,
                RowCount = categories.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Bar chart");
            return Result.Failure<ChartExecutionResult>($"Bar chart execution failed: {ex.Message}");
        }
    }

    private string BuildBarSQL(string csvPath, ChartRecommendation recommendation)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var agg = recommendation.Query.Y.Aggregation!.Value;

        var aggFunction = agg switch
        {
            Aggregation.Sum => "SUM",
            Aggregation.Avg => "AVG",
            Aggregation.Count => "COUNT",
            Aggregation.Min => "MIN",
            Aggregation.Max => "MAX",
            _ => "AVG"
        };

        var escapedPath = csvPath.Replace("'", "''");

        // TopN configurável (default 20)
        var topN = _settings.BarChartTopN > 0 ? _settings.BarChartTopN : 20;

        // GROUP BY + agregação + ORDER BY + LIMIT
        var sql = $@"
SELECT 
    CAST(""{xCol}"" AS VARCHAR) AS category,
    {aggFunction}(CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS value
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL
GROUP BY 1
ORDER BY 2 DESC
LIMIT {topN};
";

        return sql;
    }

    private async Task<Result<List<CategoryValue>>> ExecuteBarQueryAsync(
        string sql,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        var values = new List<CategoryValue>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            _logger.LogDebug("Executing DuckDB Bar query: {SQL}", sql);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                    break;

                var category = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var value = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);

                values.Add(new CategoryValue(category, value));
            }

            _logger.LogInformation("DuckDB Bar query returned {RowCount} categories", values.Count);

            return Result.Success(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB Bar query execution failed");
            return Result.Failure<List<CategoryValue>>($"Bar query execution failed: {ex.Message}");
        }
    }

    private EChartsOption BuildBarEChartsOption(
        ChartRecommendation recommendation,
        List<CategoryValue> data)
    {
        var categories = data.Select(d => d.Category).ToList();
        var values = data.Select(d => d.Value).ToList();

        var option = new EChartsOption
        {
            Title = new Dictionary<string, object>
            {
                ["text"] = recommendation.Title,
                ["subtext"] = recommendation.Reason
            },
            Tooltip = new Dictionary<string, object>
            {
                ["trigger"] = "axis",
                ["axisPointer"] = new Dictionary<string, object>
                {
                    ["type"] = "shadow"
                }
            },
            Grid = new Dictionary<string, object>
            {
                ["left"] = "3%",
                ["right"] = "4%",
                ["bottom"] = "10%",
                ["top"] = "15%",
                ["containLabel"] = true
            },
            XAxis = new Dictionary<string, object>
            {
                ["type"] = "category",
                ["name"] = recommendation.Query.X.Column,
                ["data"] = categories
            },
            YAxis = new Dictionary<string, object>
            {
                ["type"] = "value",
                ["name"] = recommendation.Query.Y.Column
            },
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = $"{recommendation.Query.Y.Aggregation}({recommendation.Query.Y.Column})",
                    ["type"] = "bar",
                    ["data"] = values
                }
            }
        };

        return option;
    }

    // ===========================
    // SCATTER CHART EXECUTION
    // ===========================

    private async Task<Result<ChartExecutionResult>> ExecuteScatterAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        await Task.CompletedTask;
        // TODO: Implementar Day 5 - Task 5.4
        return Result.Failure<ChartExecutionResult>("Scatter chart not implemented yet.");
    }

    // ===========================
    // HISTOGRAM CHART EXECUTION
    // ===========================

    private async Task<Result<ChartExecutionResult>> ExecuteHistogramAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        await Task.CompletedTask;
        // TODO: Implementar Day 5 - Task 5.5
        return Result.Failure<ChartExecutionResult>("Histogram chart not implemented yet.");
    }

    // ===========================
    // HELPERS
    // ===========================

    private async Task<Result<List<TimeSeriesPoint>>> ExecuteQueryAsync(
        string sql,
        CancellationToken ct)
    {
        await Task.CompletedTask; // DuckDB é síncrono, mas mantemos async para future-proof

        var points = new List<TimeSeriesPoint>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            _logger.LogDebug("Executing DuckDB query: {SQL}", sql);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                    break;

                // Ler timestamp (coluna 0)
                var timestamp = reader.GetDateTime(0);
                var timestampMs = new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds();

                // Ler valor agregado (coluna 1)
                var value = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);

                points.Add(new TimeSeriesPoint(timestampMs, value));
            }

            _logger.LogInformation("DuckDB query returned {RowCount} points", points.Count);

            return Result.Success(points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB query execution failed");
            return Result.Failure<List<TimeSeriesPoint>>($"Query execution failed: {ex.Message}");
        }
    }

    private string BuildTimeSeriesSQL(string csvPath, ChartRecommendation recommendation)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var bin = recommendation.Query.X.Bin!.Value;
        var agg = recommendation.Query.Y.Aggregation!.Value;

        // Mapear TimeBin para date_trunc
        var dateTruncPart = bin switch
        {
            TimeBin.Day => "day",
            TimeBin.Month => "month",
            TimeBin.Year => "year",
            _ => "day"
        };

        // Mapear Aggregation para SQL
        var aggFunction = agg switch
        {
            Aggregation.Sum => "SUM",
            Aggregation.Avg => "AVG",
            Aggregation.Count => "COUNT",
            Aggregation.Min => "MIN",
            Aggregation.Max => "MAX",
            _ => "AVG"
        };

        // Escapar o path do CSV (importante para segurança)
        // DuckDB aceita single quotes no path e escapa aspas internas duplicando-as
        var escapedPath = csvPath.Replace("'", "''");

        // Montar SQL com path do CSV inline (DuckDB não suporta parâmetros em read_csv_auto)
        // Usa COALESCE com TRY_STRPTIME para tentar múltiplos formatos de data comuns em CSVs brasileiros
        // CAST para VARCHAR primeiro para evitar erro "Binder Error: No function matches... BIGINT"
        var sql = $@"
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
    WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL
GROUP BY 1
ORDER BY 1;
";

        return sql;
    }

    /// <summary>
    /// Aplica preenchimento de lacunas (gap filling) baseado na configuração
    /// </summary>
    private List<(long TimestampMs, double? Value)> ApplyGapFilling(
        List<TimeSeriesPoint> points,
        TimeBin timeBin)
    {
        if (_settings.GapFillMode == GapFillMode.None)
        {
            return points.Select(p => (p.TimestampMs, (double?)p.Value)).ToList();
        }

        // Converter para formato do helper
        var inputPoints = points.Select(p => (p.TimestampMs, p.Value)).ToList();
        
        // Aplicar gap filling
        return GapFillHelper.FillGaps(inputPoints, _settings.GapFillMode, timeBin);
    }

    private EChartsOption BuildEChartsOption(
        ChartRecommendation recommendation,
        List<(long TimestampMs, double? Value)> data)
    {
        var rowCount = data.Count;

        // Partir do template (se existir) ou criar novo
        var option = new EChartsOption
        {
            Title = new Dictionary<string, object>
            {
                ["text"] = recommendation.Title,
                ["subtext"] = recommendation.Reason
            },
            Tooltip = new Dictionary<string, object>
            {
                ["trigger"] = "axis",
                ["axisPointer"] = new Dictionary<string, object>
                {
                    ["type"] = "cross"
                }
            },
            // Grid com defaults úteis (Prompt 4)
            Grid = new Dictionary<string, object>
            {
                ["left"] = "3%",
                ["right"] = "4%",
                ["bottom"] = "10%",
                ["top"] = "15%",
                ["containLabel"] = true
            },
            XAxis = new Dictionary<string, object>
            {
                ["type"] = "time",
                ["name"] = recommendation.Query.X.Column
            },
            YAxis = new Dictionary<string, object>
            {
                ["type"] = "value",
                ["name"] = recommendation.Query.Y.Column
            },
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = $"{recommendation.Query.Y.Aggregation}({recommendation.Query.Y.Column})",
                    ["type"] = "line",
                    ["smooth"] = true,
                    ["data"] = data.Select(p => new object?[] { p.TimestampMs, p.Value }).ToList()
                }
            }
        };

        // DataZoom automático se exceder threshold (Prompt 4)
        if (_settings.EnableAutoDataZoom && rowCount > _settings.DataZoomThreshold)
        {
            option.DataZoom = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["type"] = "slider",
                    ["show"] = true,
                    ["xAxisIndex"] = 0,
                    ["start"] = 0,
                    ["end"] = 100
                },
                new()
                {
                    ["type"] = "inside",
                    ["xAxisIndex"] = 0,
                    ["start"] = 0,
                    ["end"] = 100
                }
            };
        }

        return option;
    }

    /// <summary>
    /// Representa um ponto em série temporal
    /// </summary>
    private record TimeSeriesPoint(long TimestampMs, double Value);

    /// <summary>
    /// Representa um par categoria-valor para gráficos de barra
    /// </summary>
    private record CategoryValue(string Category, double Value);
}
