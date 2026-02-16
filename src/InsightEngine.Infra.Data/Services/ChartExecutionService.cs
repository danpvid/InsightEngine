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
using System.Linq;

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

        _logger.LogInformation(
            "📈 ExecuteLineAsync - RecommendationId: {RecId}, Query.X.Bin: {Bin}, Query.Y.Aggregation: {Agg}, Query.Y.Column: {YCol}",
            recommendation.Id, recommendation.Query.X.Bin, recommendation.Query.Y.Aggregation, recommendation.Query.Y.Column);

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
            var maxPoints = _settings.TimeSeriesMaxPoints > 0 ? _settings.TimeSeriesMaxPoints : 2000;

            if (recommendation.Query.Series != null)
            {
                var dataResult = await ExecuteGroupedTimeSeriesQueryAsync(sql, ct);
                swDuckDb.Stop();

                if (!dataResult.IsSuccess)
                    return Result.Failure<ChartExecutionResult>(dataResult.Errors);

                var grouped = dataResult.Data!;
                var seriesData = new Dictionary<string, List<(long TimestampMs, double? Value)>>();

                foreach (var group in grouped.GroupBy(p => string.IsNullOrWhiteSpace(p.Series) ? "Unknown" : p.Series))
                {
                    var rawPoints = group
                        .Select(p => new TimeSeriesPoint(p.TimestampMs, p.Value))
                        .ToList();

                    var processed = ApplyGapFilling(rawPoints, recommendation.Query.X.Bin!.Value);
                    processed = DownsampleSeries(processed, maxPoints);
                    seriesData[group.Key] = processed;
                }

                var option = BuildEChartsOption(recommendation, seriesData);
                var rowCount = seriesData.Sum(s => s.Value.Count);

                return Result.Success(new ChartExecutionResult
                {
                    Option = option,
                    DuckDbMs = swDuckDb.ElapsedMilliseconds,
                    GeneratedSql = sql,
                    RowCount = rowCount
                });
            }

            var singleResult = await ExecuteQueryAsync(sql, ct);
            swDuckDb.Stop();

            if (!singleResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(singleResult.Errors);

            // Aplicar gap filling
            var processedPoints = ApplyGapFilling(singleResult.Data!, recommendation.Query.X.Bin!.Value);
            processedPoints = DownsampleSeries(processedPoints, maxPoints);

            // Montar ECharts option
            var optionSingle = BuildEChartsOption(recommendation, processedPoints);

            return Result.Success(new ChartExecutionResult
            {
                Option = optionSingle,
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

            if (recommendation.Query.Series != null)
            {
                var dataResult = await ExecuteGroupedBarQueryAsync(sql, ct);
                swDuckDb.Stop();

                if (!dataResult.IsSuccess)
                    return Result.Failure<ChartExecutionResult>(dataResult.Errors);

                var grouped = dataResult.Data!;
                var option = BuildBarEChartsOption(recommendation, grouped);

                return Result.Success(new ChartExecutionResult
                {
                    Option = option,
                    DuckDbMs = swDuckDb.ElapsedMilliseconds,
                    GeneratedSql = sql,
                    RowCount = grouped.Count
                });
            }

            var singleResult = await ExecuteBarQueryAsync(sql, ct);
            swDuckDb.Stop();

            if (!singleResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(singleResult.Errors);

            var categories = singleResult.Data!;

            // Montar ECharts option
            var optionSingle = BuildBarEChartsOption(recommendation, categories);

            return Result.Success(new ChartExecutionResult
            {
                Option = optionSingle,
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
        var seriesCol = recommendation.Query.Series?.Column;

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
        var filterClause = BuildFilterClause(recommendation.Query.Filters);

        // TopN configurável (default 20)
        var topN = _settings.BarChartTopN > 0 ? _settings.BarChartTopN : 20;

        // GROUP BY + agregação + ORDER BY + LIMIT
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
LIMIT {topN};
";
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
LIMIT {groupedLimit};
";
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

    private async Task<Result<List<CategorySeriesValue>>> ExecuteGroupedBarQueryAsync(
        string sql,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        var values = new List<CategorySeriesValue>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            _logger.LogDebug("Executing DuckDB grouped Bar query: {SQL}", sql);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                    break;

                var category = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var series = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var value = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);

                values.Add(new CategorySeriesValue(category, series, value));
            }

            _logger.LogInformation("DuckDB grouped Bar query returned {RowCount} rows", values.Count);

            return Result.Success(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB grouped Bar query execution failed");
            return Result.Failure<List<CategorySeriesValue>>($"Bar query execution failed: {ex.Message}");
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
        var swDuckDb = Stopwatch.StartNew();

        try
        {
            // Validar eixos: X=Measure, Y=Measure (sem agregação)
            if (recommendation.Query.X.Role != AxisRole.Measure)
                return Result.Failure<ChartExecutionResult>("Scatter chart requires X axis with role Measure.");

            if (recommendation.Query.Y.Role != AxisRole.Measure)
                return Result.Failure<ChartExecutionResult>("Scatter chart requires Y axis with role Measure.");

            // Scatter não usa agregação (pontos individuais)
            if (recommendation.Query.X.Aggregation.HasValue || recommendation.Query.Y.Aggregation.HasValue)
                return Result.Failure<ChartExecutionResult>("Scatter chart does not use aggregation (plots individual points).");

            // Gerar e executar SQL com sampling
            var sql = BuildScatterSQL(csvPath, recommendation);
            var dataResult = await ExecuteScatterQueryAsync(sql, ct);
            swDuckDb.Stop();

            if (!dataResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(dataResult.Errors);

            var points = dataResult.Data!;

            // Montar ECharts option
            var option = BuildScatterEChartsOption(recommendation, points);

            return Result.Success(new ChartExecutionResult
            {
                Option = option,
                DuckDbMs = swDuckDb.ElapsedMilliseconds,
                GeneratedSql = sql,
                RowCount = points.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Scatter chart");
            return Result.Failure<ChartExecutionResult>($"Scatter chart execution failed: {ex.Message}");
        }
    }

    private string BuildScatterSQL(string csvPath, ChartRecommendation recommendation)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;

        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);

        // Task 6.5: Enforce safety limit for scatter points (max 2000)
        var maxPoints = _settings.ScatterMaxPoints > 0 ? _settings.ScatterMaxPoints : 2000;
        
        _logger.LogDebug("Scatter max points: {MaxPoints}", maxPoints);

        // Scatter: pontos individuais (sem agregação) + sampling aleatório se muitos pontos
        var sql = $@"
SELECT 
    CAST(REPLACE(CAST(""{xCol}"" AS VARCHAR), ',', '') AS DOUBLE) AS x,
    CAST(REPLACE(CAST(""{yCol}"" AS VARCHAR), ',', '') AS DOUBLE) AS y
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL AND ""{yCol}"" IS NOT NULL{filterClause}
ORDER BY random()
LIMIT {maxPoints};
";

        return sql;
    }

    private async Task<Result<List<ScatterPoint>>> ExecuteScatterQueryAsync(
        string sql,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        var points = new List<ScatterPoint>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            _logger.LogDebug("Executing DuckDB Scatter query: {SQL}", sql);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                    break;

                var x = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                var y = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);

                points.Add(new ScatterPoint(x, y));
            }

            _logger.LogInformation("DuckDB Scatter query returned {RowCount} points", points.Count);

            return Result.Success(points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB Scatter query execution failed");
            return Result.Failure<List<ScatterPoint>>($"Scatter query execution failed: {ex.Message}");
        }
    }

    private EChartsOption BuildScatterEChartsOption(
        ChartRecommendation recommendation,
        List<ScatterPoint> data)
    {
        // Converter para formato ECharts [[x, y], [x, y], ...]
        var scatterData = data.Select(p => new object[] { p.X, p.Y }).ToList();

        var option = new EChartsOption
        {
            Title = new Dictionary<string, object>
            {
                ["text"] = recommendation.Title,
                ["subtext"] = recommendation.Reason
            },
            Tooltip = new Dictionary<string, object>
            {
                ["trigger"] = "item",
                ["formatter"] = "{a}<br/>{b}: ({c})"
            },
            Grid = new Dictionary<string, object>
            {
                ["left"] = "3%",
                ["right"] = "7%",
                ["bottom"] = "10%",
                ["top"] = "15%",
                ["containLabel"] = true
            },
            XAxis = new Dictionary<string, object>
            {
                ["type"] = "value",
                ["name"] = recommendation.Query.X.Column,
                ["scale"] = true  // Auto-escala baseado nos dados
            },
            YAxis = new Dictionary<string, object>
            {
                ["type"] = "value",
                ["name"] = recommendation.Query.Y.Column,
                ["scale"] = true
            },
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = $"{recommendation.Query.X.Column} vs {recommendation.Query.Y.Column}",
                    ["type"] = "scatter",
                    ["symbolSize"] = 8,
                    ["data"] = scatterData
                }
            }
        };

        return option;
    }

    // ===========================
    // HISTOGRAM CHART EXECUTION
    // ===========================

    private async Task<Result<ChartExecutionResult>> ExecuteHistogramAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        var swDuckDb = Stopwatch.StartNew();

        try
        {
            // Validar eixo: apenas X=Measure (histograma de distribuição)
            if (recommendation.Query.X.Role != AxisRole.Measure)
                return Result.Failure<ChartExecutionResult>("Histogram chart requires X axis with role Measure.");

            // Histograma não usa Y axis
            if (recommendation.Query.Y != null && !string.IsNullOrEmpty(recommendation.Query.Y.Column))
                return Result.Failure<ChartExecutionResult>("Histogram chart uses only X axis (frequency distribution).");

            // Gerar e executar SQL para calcular bins
            var histogramResult = await ExecuteHistogramQueryAsync(csvPath, recommendation, ct);
            swDuckDb.Stop();

            if (!histogramResult.IsSuccess)
                return Result.Failure<ChartExecutionResult>(histogramResult.Errors);

            var (bins, sql) = histogramResult.Data!;

            // Montar ECharts option
            var option = BuildHistogramEChartsOption(recommendation, bins);

            return Result.Success(new ChartExecutionResult
            {
                Option = option,
                DuckDbMs = swDuckDb.ElapsedMilliseconds,
                GeneratedSql = sql,
                RowCount = bins.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Histogram chart");
            return Result.Failure<ChartExecutionResult>($"Histogram chart execution failed: {ex.Message}");
        }
    }

    private async Task<Result<(List<HistogramBin> Bins, string Sql)>> ExecuteHistogramQueryAsync(
        string csvPath,
        ChartRecommendation recommendation,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        var xCol = recommendation.Query.X.Column;
        var escapedPath = csvPath.Replace("'", "''");
        var filterClause = BuildFilterClause(recommendation.Query.Filters);
        
        // Task 6.5: Enforce safety limits for bins (5-50)
        var numBins = _settings.HistogramBins > 0 ? _settings.HistogramBins : 20;
        var minBins = _settings.HistogramMinBins > 0 ? _settings.HistogramMinBins : 5;
        var maxBins = _settings.HistogramMaxBins > 0 ? _settings.HistogramMaxBins : 50;
        
        // Clamp bins to safe range
        numBins = Math.Max(minBins, Math.Min(maxBins, numBins));
        
        _logger.LogDebug("Histogram bins: {NumBins} (min: {MinBins}, max: {MaxBins})", numBins, minBins, maxBins);

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            // Passo 1: Obter min e max
            var minMaxSql = $@"
SELECT 
    MIN(CAST(REPLACE(CAST(""{xCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS min_val,
    MAX(CAST(REPLACE(CAST(""{xCol}"" AS VARCHAR), ',', '') AS DOUBLE)) AS max_val
FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
WHERE ""{xCol}"" IS NOT NULL{filterClause};
";

            double minVal, maxVal;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = minMaxSql;
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return Result.Failure<(List<HistogramBin>, string)>("Failed to calculate min/max for histogram.");

                minVal = reader.IsDBNull(0) ? 0.0 : reader.GetDouble(0);
                maxVal = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
            }

            // Evitar divisão por zero
            if (Math.Abs(maxVal - minVal) < 0.0001)
            {
                return Result.Success((
                    new List<HistogramBin> { new($"{minVal:F2}", 1) },
                    minMaxSql
                ));
            }

            var binWidth = (maxVal - minVal) / numBins;

            // Passo 2: Calcular contagens por bin
            // Use InvariantCulture para evitar problemas com separador decimal
            var minValStr = minVal.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            var maxValStr = maxVal.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            var binWidthStr = binWidth.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            
            var histogramSql = $@"
SELECT 
    FLOOR((value - {minValStr}) / {binWidthStr}) AS bin_index,
    COUNT(*) AS count
FROM (
    SELECT CAST(REPLACE(CAST(""{xCol}"" AS VARCHAR), ',', '') AS DOUBLE) AS value
    FROM read_csv_auto('{escapedPath}', header=true, ignore_errors=true)
    WHERE ""{xCol}"" IS NOT NULL{filterClause}
)
WHERE value >= {minValStr} AND value <= {maxValStr}
GROUP BY 1
ORDER BY 1;
";

            var bins = new List<HistogramBin>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = histogramSql;

                _logger.LogDebug("Executing DuckDB Histogram query: {SQL}", histogramSql);

                using var reader = cmd.ExecuteReader();

                // Criar todos os bins (preenchendo com 0 se não houver dados)
                var binCounts = new Dictionary<int, int>();
                while (reader.Read())
                {
                    if (ct.IsCancellationRequested)
                        break;

                    var binIndex = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    var count = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

                    binCounts[binIndex] = count;
                }

                // Gerar labels e contagens para todos os bins
                for (int i = 0; i < numBins; i++)
                {
                    var binStart = minVal + (i * binWidth);
                    var binEnd = minVal + ((i + 1) * binWidth);
                    var label = $"[{binStart:F2}, {binEnd:F2})";
                    var count = binCounts.GetValueOrDefault(i, 0);

                    bins.Add(new HistogramBin(label, count));
                }
            }

            _logger.LogInformation("DuckDB Histogram query returned {BinCount} bins", bins.Count);

            var combinedSql = $"-- Step 1: Min/Max\n{minMaxSql}\n\n-- Step 2: Bin Counts\n{histogramSql}";
            return Result.Success((bins, combinedSql));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB Histogram query execution failed. minVal={MinVal}, maxVal={MaxVal}, binWidth={BinWidth}, numBins={NumBins}",
                0, 0, 0, numBins);
            return Result.Failure<(List<HistogramBin>, string)>($"Histogram query execution failed: {ex.Message}");
        }
    }

    private EChartsOption BuildHistogramEChartsOption(
        ChartRecommendation recommendation,
        List<HistogramBin> bins)
    {
        var labels = bins.Select(b => b.Label).ToList();
        var counts = bins.Select(b => b.Count).ToList();

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
                ["data"] = labels,
                ["axisLabel"] = new Dictionary<string, object>
                {
                    ["rotate"] = 45,
                    ["fontSize"] = 10
                }
            },
            YAxis = new Dictionary<string, object>
            {
                ["type"] = "value",
                ["name"] = "Frequency"
            },
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["name"] = "Frequency",
                    ["type"] = "bar",
                    ["data"] = counts,
                    ["itemStyle"] = new Dictionary<string, object>
                    {
                        ["color"] = "#5470c6"
                    }
                }
            }
        };

        return option;
    }

    // ===========================
    // HELPERS
    // ===========================

    private string BuildFilterClause(IReadOnlyCollection<ChartFilter> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return string.Empty;
        }

        var combined = BuildCombinedFilterExpression(filters, BuildFilterExpression);
        return string.IsNullOrWhiteSpace(combined) ? string.Empty : $" AND {combined}";
    }

    private string BuildFilterExpression(ChartFilter filter)
    {
        if (filter.Values.Count == 0)
        {
            return string.Empty;
        }

        var columnExpr = $"CAST(\"{filter.Column}\" AS VARCHAR)";

        switch (filter.Operator)
        {
            case FilterOperator.Contains:
                var pattern = $"%{filter.Values[0]}%";
                return $"LOWER({columnExpr}) LIKE {ToSqlLiteral(pattern.ToLowerInvariant())}";
            case FilterOperator.Eq:
                return BuildComparison(columnExpr, "=", filter.Values);
            case FilterOperator.NotEq:
                return BuildComparison(columnExpr, "<>", filter.Values);
            case FilterOperator.Gt:
                return BuildComparison(columnExpr, ">", filter.Values);
            case FilterOperator.Gte:
                return BuildComparison(columnExpr, ">=", filter.Values);
            case FilterOperator.Lt:
                return BuildComparison(columnExpr, "<", filter.Values);
            case FilterOperator.Lte:
                return BuildComparison(columnExpr, "<=", filter.Values);
            case FilterOperator.In:
                return BuildInClause(columnExpr, filter.Values);
            case FilterOperator.Between:
                return BuildBetweenClause(columnExpr, filter.Values);
            default:
                return string.Empty;
        }
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
            if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                numbers.Clear();
                return false;
            }

            numbers.Add(number);
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

    private static string BuildParsedDateExpression(string columnExpr)
    {
        return $@"COALESCE(
            TRY_CAST({columnExpr} AS TIMESTAMP),
            TRY_STRPTIME({columnExpr}, '%Y%m%d'),
            TRY_STRPTIME({columnExpr}, '%d/%m/%Y'),
            TRY_STRPTIME({columnExpr}, '%Y-%m-%d'),
            TRY_STRPTIME({columnExpr}, '%m/%d/%Y')
        )";
    }

    private static string ToSqlLiteral(string value)
    {
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
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

    private async Task<Result<List<GroupedTimeSeriesPoint>>> ExecuteGroupedTimeSeriesQueryAsync(
        string sql,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        var points = new List<GroupedTimeSeriesPoint>();

        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            _logger.LogDebug("Executing DuckDB grouped query: {SQL}", sql);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                if (ct.IsCancellationRequested)
                    break;

                var timestamp = reader.GetDateTime(0);
                var timestampMs = new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var series = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var value = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);

                points.Add(new GroupedTimeSeriesPoint(timestampMs, series, value));
            }

            _logger.LogInformation("DuckDB grouped query returned {RowCount} points", points.Count);

            return Result.Success(points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DuckDB grouped query execution failed");
            return Result.Failure<List<GroupedTimeSeriesPoint>>($"Query execution failed: {ex.Message}");
        }
    }

    private string BuildTimeSeriesSQL(string csvPath, ChartRecommendation recommendation)
    {
        var xCol = recommendation.Query.X.Column;
        var yCol = recommendation.Query.Y.Column;
        var bin = recommendation.Query.X.Bin!.Value;
        var agg = recommendation.Query.Y.Aggregation!.Value;
        var seriesCol = recommendation.Query.Series?.Column;
        var filterClause = BuildFilterClause(recommendation.Query.Filters);

        _logger.LogInformation(
            "🔨 BuildTimeSeriesSQL - XCol: {XCol}, YCol: {YCol}, Bin: {Bin}, Agg: {Agg}",
            xCol, yCol, bin, agg);

        // Mapear TimeBin para date_trunc
        var dateTruncPart = bin switch
        {
            TimeBin.Day => "day",
            TimeBin.Week => "week",
            TimeBin.Month => "month",
            TimeBin.Quarter => "quarter",
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

        _logger.LogInformation(
            "🔧 SQL Mapping - dateTruncPart: {DateTrunc}, aggFunction: {AggFunc}",
            dateTruncPart, aggFunction);

        // Escapar o path do CSV (importante para segurança)
        // DuckDB aceita single quotes no path e escapa aspas internas duplicando-as
        var escapedPath = csvPath.Replace("'", "''");

        // Montar SQL com path do CSV inline (DuckDB não suporta parâmetros em read_csv_auto)
        // Usa COALESCE com TRY_STRPTIME para tentar múltiplos formatos de data comuns em CSVs brasileiros
        // CAST para VARCHAR primeiro para evitar erro "Binder Error: No function matches... BIGINT"
        string sql;
        if (string.IsNullOrWhiteSpace(seriesCol))
        {
            sql = $@"
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
ORDER BY 1;
";
        }
        else
        {
            sql = $@"
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
ORDER BY 1, 2;
";
        }

        _logger.LogInformation("💾 Generated SQL:\n{SQL}", sql);

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

    private List<(long TimestampMs, double? Value)> DownsampleSeries(
        List<(long TimestampMs, double? Value)> data,
        int maxPoints)
    {
        if (maxPoints <= 0 || data.Count <= maxPoints)
        {
            return data;
        }

        var step = (int)Math.Ceiling(data.Count / (double)maxPoints);
        var sampled = new List<(long TimestampMs, double? Value)>();

        for (var i = 0; i < data.Count; i += step)
        {
            sampled.Add(data[i]);
        }

        return sampled;
    }

    private EChartsOption BuildEChartsOption(
        ChartRecommendation recommendation,
        Dictionary<string, List<(long TimestampMs, double? Value)>> seriesData)
    {
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
            Legend = new Dictionary<string, object>
            {
                ["data"] = seriesData.Keys.ToList()
            },
            Series = new List<Dictionary<string, object>>()
        };

        foreach (var series in seriesData)
        {
            option.Series!.Add(new Dictionary<string, object>
            {
                ["name"] = series.Key,
                ["type"] = "line",
                ["smooth"] = true,
                ["connectNulls"] = true,
                ["data"] = series.Value.Select(p => new object?[] { p.TimestampMs, p.Value }).ToList()
            });
        }

        var maxCount = seriesData.Values.Select(s => s.Count).DefaultIfEmpty(0).Max();
        if (_settings.EnableAutoDataZoom && maxCount > _settings.DataZoomThreshold)
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

    private EChartsOption BuildBarEChartsOption(
        ChartRecommendation recommendation,
        List<CategorySeriesValue> data)
    {
        var categories = new List<string>();
        var categorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in data)
        {
            if (categorySet.Add(item.Category))
            {
                categories.Add(item.Category);
            }
        }

        var seriesTotals = data
            .GroupBy(d => d.Series)
            .Select(g => new { Series = g.Key, Total = g.Sum(x => x.Value) })
            .OrderByDescending(g => g.Total)
            .Take(5)
            .Select(g => g.Series)
            .ToList();

        var seriesList = new List<Dictionary<string, object>>();

        foreach (var series in seriesTotals)
        {
            var values = categories
                .Select(cat => data.FirstOrDefault(d => d.Category == cat && d.Series == series)?.Value ?? 0.0)
                .ToList();

            seriesList.Add(new Dictionary<string, object>
            {
                ["name"] = series,
                ["type"] = "bar",
                ["data"] = values
            });
        }

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
            Legend = new Dictionary<string, object>
            {
                ["data"] = seriesTotals
            },
            Grid = new Dictionary<string, object>
            {
                ["left"] = "3%",
                ["right"] = "4%",
                ["bottom"] = "10%",
                ["top"] = "18%",
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
            Series = seriesList
        };

        return option;
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
                    ["connectNulls"] = true,  // Conecta pontos mesmo com gaps
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
    private record GroupedTimeSeriesPoint(long TimestampMs, string Series, double Value);

    /// <summary>
    /// Representa um par categoria-valor para gráficos de barra
    /// </summary>
    private record CategoryValue(string Category, double Value);
    private record CategorySeriesValue(string Category, string Series, double Value);

    /// <summary>
    /// Representa um ponto (x, y) para gráfico de dispersão
    /// </summary>
    private record ScatterPoint(double X, double Y);

    /// <summary>
    /// Representa um bin de histograma com label e contagem
    /// </summary>
    private record HistogramBin(string Label, int Count);
}
