using DuckDB.NET.Data;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;
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

    public ChartExecutionService(
        IFileStorageService fileStorageService,
        ILogger<ChartExecutionService> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<Result<EChartsOption>> ExecuteAsync(
        Guid datasetId,
        ChartRecommendation recommendation,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Validar suporte (Dia 4: apenas Line + ECharts)
            var validationErrors = ValidateRecommendation(recommendation);
            if (validationErrors.Any())
                return Result.Failure<EChartsOption>(validationErrors);

            // 2. Resolver path do CSV
            var csvPath = _fileStorageService.GetFullPath($"{datasetId}.csv");
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("Dataset file not found: {DatasetId}", datasetId);
                return Result.Failure<EChartsOption>($"Dataset file not found: {datasetId}");
            }

            // 3. Executar query no DuckDB
            var dataResult = await ExecuteQueryAsync(csvPath, recommendation, ct);
            if (!dataResult.IsSuccess)
                return Result.Failure<EChartsOption>(dataResult.Errors);

            // 4. Montar EChartsOption completo
            var option = BuildEChartsOption(recommendation, dataResult.Data!);

            sw.Stop();
            _logger.LogInformation(
                "Chart executed successfully: {RecommendationId}, Rows: {RowCount}, Duration: {Duration}ms",
                recommendation.Id, dataResult.Data!.Count, sw.ElapsedMilliseconds);

            return Result.Success(option);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error executing chart: {RecommendationId}", recommendation.Id);
            return Result.Failure<EChartsOption>($"Error executing chart: {ex.Message}");
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
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        await Task.CompletedTask; // DuckDB é síncrono, mas mantemos async para future-proof

        var points = new List<TimeSeriesPoint>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();

            // Montar SQL para time series com path do CSV embutido
            var sql = BuildTimeSeriesSQL(csvPath, recommendation);
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
        var sql = $@"
SELECT 
    date_trunc('{dateTruncPart}', parsed_date) AS x,
    {aggFunction}(parsed_value) AS y
FROM (
    SELECT 
        COALESCE(
            TRY_CAST(""{xCol}"" AS TIMESTAMP),
            TRY_STRPTIME(""{xCol}"", '%Y%m%d'),
            TRY_STRPTIME(""{xCol}"", '%d/%m/%Y'),
            TRY_STRPTIME(""{xCol}"", '%Y-%m-%d'),
            TRY_STRPTIME(""{xCol}"", '%m/%d/%Y')
        ) AS parsed_date,
        CAST(REPLACE(""{yCol}"", ',', '') AS DOUBLE) AS parsed_value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL
)
WHERE parsed_date IS NOT NULL AND parsed_value IS NOT NULL
GROUP BY 1
ORDER BY 1;
";

        return sql;
    }

    private EChartsOption BuildEChartsOption(
        ChartRecommendation recommendation,
        List<TimeSeriesPoint> data)
    {
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
                    ["data"] = data.Select(p => new object[] { p.TimestampMs, p.Value }).ToList()
                }
            }
        };

        return option;
    }

    /// <summary>
    /// Representa um ponto em série temporal
    /// </summary>
    private record TimeSeriesPoint(long TimestampMs, double Value);
}
