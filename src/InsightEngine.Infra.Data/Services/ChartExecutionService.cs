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
        string generatedSql = string.Empty;
        long duckDbMs = 0;

        try
        {
            // 1. Validar suporte (Dia 4: apenas Line + ECharts)
            var validationErrors = ValidateRecommendation(recommendation);
            if (validationErrors.Any())
                return Result.Failure<ChartExecutionResult>(validationErrors);

            // 2. Resolver path do CSV
            var csvPath = _fileStorageService.GetFullPath($"{datasetId}.csv");
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("Dataset file not found: {DatasetId}", datasetId);
                return Result.Failure<ChartExecutionResult>($"Dataset file not found: {datasetId}");
            }

            // 3. Gerar SQL
            generatedSql = BuildTimeSeriesSQL(csvPath, recommendation);

            // 4. Executar query no DuckDB (medir tempo separadamente)
            var swDuckDb = Stopwatch.StartNew();
            var dataResult = await ExecuteQueryAsync(generatedSql, ct);
            swDuckDb.Stop();
            duckDbMs = swDuckDb.ElapsedMilliseconds;

            if (!dataResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(dataResult.Errors);

            // 4.5. Aplicar gap filling se configurado
            var processedPoints = ApplyGapFilling(dataResult.Data!, recommendation.Query.X.Bin!.Value);

            // 5. Montar EChartsOption completo
            var option = BuildEChartsOption(recommendation, processedPoints);

            sw.Stop();

            var result = new ChartExecutionResult
            {
                Option = option,
                DuckDbMs = duckDbMs,
                GeneratedSql = generatedSql,
                RowCount = processedPoints.Count
            };

            _logger.LogInformation(
                "Chart executed successfully: {RecommendationId}, Rows: {RowCount}, TotalMs: {TotalMs}, DuckDbMs: {DuckDbMs}, GapFillMode: {GapFillMode}",
                recommendation.Id, result.RowCount, sw.ElapsedMilliseconds, duckDbMs, _settings.GapFillMode);

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error executing chart: {RecommendationId}", recommendation.Id);
            return Result.Failure<ChartExecutionResult>($"Error executing chart: {ex.Message}");
        }
    }

    private List<string> ValidateRecommendation(ChartRecommendation recommendation)
    {
        var errors = new List<string>();

        // Validar biblioteca
        if (recommendation.Chart.Library != ChartLibrary.ECharts)
            errors.Add($"Unsupported chart library: {recommendation.Chart.Library}. Day 4 supports only ECharts.");

        // Validar tipo de gráfico
        if (recommendation.Chart.Type != ChartType.Line)
            errors.Add($"Unsupported chart type: {recommendation.Chart.Type}. Day 4 supports only Line charts.");

        // Validar eixo X (deve ser time com bin)
        if (recommendation.Query.X.Role != AxisRole.Time)
            errors.Add("X axis must have role Time for time series charts.");

        if (!recommendation.Query.X.Bin.HasValue)
            errors.Add("X axis must have TimeBin defined (Day, Month, Year).");

        // Validar eixo Y (deve ter agregação)
        if (recommendation.Query.Y.Role != AxisRole.Measure)
            errors.Add("Y axis must have role Measure.");

        if (!recommendation.Query.Y.Aggregation.HasValue)
            errors.Add("Y axis must have aggregation defined (Sum, Avg, Count, Min, Max).");

        return errors;
    }

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
}
