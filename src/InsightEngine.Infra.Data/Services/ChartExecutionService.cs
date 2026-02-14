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
            var validationResult = ValidateRecommendation(recommendation);
            if (!validationResult.IsSuccess)
                return Result<EChartsOption>.Failure<EChartsOption>(validationResult.Errors);

            // 2. Resolver path do CSV
            var csvPath = _fileStorageService.GetFullPath($"{datasetId}.csv");
            if (!File.Exists(csvPath))
            {
                _logger.LogWarning("Dataset file not found: {DatasetId}", datasetId);
                return Result<EChartsOption>.Failure<EChartsOption>($"Dataset file not found: {datasetId}");
            }

            // 3. Executar query no DuckDB
            var dataResult = await ExecuteQueryAsync(csvPath, recommendation, ct);
            if (!dataResult.IsSuccess)
                return Result<EChartsOption>.Failure<EChartsOption>(dataResult.Errors);

            // 4. Montar EChartsOption completo
            var option = BuildEChartsOption(recommendation, dataResult.Data!);

            sw.Stop();
            _logger.LogInformation(
                "Chart executed successfully: {RecommendationId}, Rows: {RowCount}, Duration: {Duration}ms",
                recommendation.Id, dataResult.Data!.Count, sw.ElapsedMilliseconds);

            return Result<EChartsOption>.Success(option);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error executing chart: {RecommendationId}", recommendation.Id);
            return Result<EChartsOption>.Failure<EChartsOption>($"Error executing chart: {ex.Message}");
        }
    }

    private Result ValidateRecommendation(ChartRecommendation recommendation)
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

        return errors.Any() ? Result.Failure(errors) : Result.Success();
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

            // Montar SQL para time series
            var sql = BuildTimeSeriesSQL(recommendation);
            command.CommandText = sql;

            // Parametrizar o path do CSV
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@csvPath";
            parameter.Value = csvPath;
            command.Parameters.Add(parameter);

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

            return Result<List<TimeSeriesPoint>>.Success(points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB query execution failed");
            return Result<List<TimeSeriesPoint>>.Failure<List<TimeSeriesPoint>>($"Query execution failed: {ex.Message}");
        }
    }

    private string BuildTimeSeriesSQL(ChartRecommendation recommendation)
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

        // Montar SQL
        var sql = $@"
SELECT 
    date_trunc('{dateTruncPart}', CAST(""{xCol}"" AS TIMESTAMP)) AS x,
    {aggFunction}(CAST(""{yCol}"" AS DOUBLE)) AS y
FROM read_csv_auto(@csvPath, header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL
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
