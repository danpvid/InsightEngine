using System.Globalization;
using System.Text;
using System.Text.Json;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Application.Services;

public class LLMContextBuilder : ILLMContextBuilder
{
    private const int MaxSamplePoints = 30;
    private readonly IDataSetApplicationService _dataSetApplicationService;
    private readonly ILLMRedactionService _redactionService;
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly ILogger<LLMContextBuilder> _logger;

    public LLMContextBuilder(
        IDataSetApplicationService dataSetApplicationService,
        ILLMRedactionService redactionService,
        IOptionsMonitor<LLMSettings> settingsMonitor,
        ILogger<LLMContextBuilder> logger)
    {
        _dataSetApplicationService = dataSetApplicationService;
        _redactionService = redactionService;
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public async Task<Result<LLMContextPayload>> BuildChartContextAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DatasetId == Guid.Empty || string.IsNullOrWhiteSpace(request.RecommendationId))
        {
            return Result.Failure<LLMContextPayload>("DatasetId and RecommendationId are required.");
        }

        var profileResult = await _dataSetApplicationService.GetProfileAsync(request.DatasetId, cancellationToken);
        if (!profileResult.IsSuccess || profileResult.Data == null)
        {
            return Result.Failure<LLMContextPayload>(profileResult.Errors);
        }

        var recommendationsResult = await _dataSetApplicationService.GetRecommendationsAsync(request.DatasetId, cancellationToken);
        if (!recommendationsResult.IsSuccess || recommendationsResult.Data == null)
        {
            return Result.Failure<LLMContextPayload>(recommendationsResult.Errors);
        }

        var recommendation = recommendationsResult.Data
            .FirstOrDefault(item => string.Equals(item.Id, request.RecommendationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation == null)
        {
            return Result.Failure<LLMContextPayload>($"Recommendation '{request.RecommendationId}' was not found.");
        }

        var chartResult = await _dataSetApplicationService.GetChartAsync(
            request.DatasetId,
            request.RecommendationId,
            request.Aggregation,
            request.TimeBin,
            request.MetricY,
            request.GroupBy,
            request.Filters,
            cancellationToken);

        if (!chartResult.IsSuccess || chartResult.Data == null)
        {
            return Result.Failure<LLMContextPayload>(chartResult.Errors);
        }

        var chartResponse = chartResult.Data;
        var context = BuildBaseContext(profileResult.Data);

        context["recommendation"] = new Dictionary<string, object?>
        {
            ["id"] = recommendation.Id,
            ["title"] = recommendation.Title,
            ["reason"] = recommendation.Reason,
            ["chartType"] = recommendation.Chart.Type,
            ["score"] = recommendation.Score,
            ["impactScore"] = recommendation.ImpactScore
        };

        context["chartMeta"] = new Dictionary<string, object?>
        {
            ["chartType"] = chartResponse.ExecutionResult.Option.Series?.FirstOrDefault()?["type"]?.ToString(),
            ["rowCountReturned"] = chartResponse.ExecutionResult.RowCount,
            ["duckDbMs"] = chartResponse.ExecutionResult.DuckDbMs
        };

        context["queryMeta"] = new Dictionary<string, object?>
        {
            ["aggregation"] = request.Aggregation ?? recommendation.Query.Y.Aggregation?.ToString(),
            ["timeBin"] = request.TimeBin ?? recommendation.Query.X.Bin?.ToString(),
            ["metricY"] = request.MetricY ?? recommendation.Query.Y.Column,
            ["groupBy"] = request.GroupBy ?? recommendation.Query.Series?.Column,
            ["filters"] = request.Filters.Select(ToFilterProjection).ToList()
        };

        context["sampleSeries"] = ExtractAggregatedSample(chartResponse.ExecutionResult.Option, MaxSamplePoints);

        if (request.ScenarioMeta.Count > 0)
        {
            context["scenarioMeta"] = request.ScenarioMeta;
        }

        var payload = new LLMContextPayload
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            QueryHash = chartResponse.QueryHash,
            HeuristicSummary = chartResponse.InsightSummary,
            ChartMeta = new ChartExecutionMeta
            {
                RowCountReturned = chartResponse.ExecutionResult.RowCount,
                DuckDbMs = chartResponse.ExecutionResult.DuckDbMs,
                ExecutionMs = chartResponse.TotalExecutionMs,
                ChartType = chartResponse.ExecutionResult.Option.Series?.FirstOrDefault()?["type"]?.ToString() ?? string.Empty,
                GeneratedAt = DateTime.UtcNow,
                QueryHash = chartResponse.QueryHash,
                CacheHit = chartResponse.CacheHit
            },
            ContextObjects = _redactionService.RedactContext(context)
        };

        EnforceBudget(payload, _settingsMonitor.CurrentValue.MaxContextBytes);
        return Result.Success(payload);
    }

    public async Task<Result<LLMContextPayload>> BuildAskContextAsync(
        LLMAskContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DatasetId == Guid.Empty)
        {
            return Result.Failure<LLMContextPayload>("DatasetId is required.");
        }

        var profileResult = await _dataSetApplicationService.GetProfileAsync(request.DatasetId, cancellationToken);
        if (!profileResult.IsSuccess || profileResult.Data == null)
        {
            return Result.Failure<LLMContextPayload>(profileResult.Errors);
        }

        var context = BuildBaseContext(profileResult.Data);
        if (request.CurrentView.Count > 0)
        {
            context["currentView"] = request.CurrentView;
        }

        var payload = new LLMContextPayload
        {
            DatasetId = request.DatasetId,
            ContextObjects = _redactionService.RedactContext(context)
        };

        EnforceBudget(payload, _settingsMonitor.CurrentValue.MaxContextBytes);
        return Result.Success(payload);
    }

    private static Dictionary<string, object?> BuildBaseContext(DatasetProfile profile)
    {
        var schema = profile.Columns
            .Select(column => new Dictionary<string, object?>
            {
                ["name"] = column.Name,
                ["type"] = column.InferredType.ToString()
            })
            .ToList();

        var profiles = profile.Columns
            .Select(column => new Dictionary<string, object?>
            {
                ["name"] = column.Name,
                ["type"] = column.InferredType.ToString(),
                ["nullRate"] = Math.Round(column.NullRate, 4),
                ["distinctCount"] = column.DistinctCount,
                ["min"] = column.Min,
                ["max"] = column.Max,
                ["topValues"] = column.TopValues.Take(5).ToList()
            })
            .ToList();

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dataset"] = new Dictionary<string, object?>
            {
                ["datasetId"] = profile.DatasetId,
                ["rowCount"] = profile.RowCount,
                ["sampleSize"] = profile.SampleSize
            },
            ["schema"] = schema,
            ["profiles"] = profiles
        };
    }

    private static Dictionary<string, object?> ToFilterProjection(ChartFilter filter)
    {
        return new Dictionary<string, object?>
        {
            ["column"] = filter.Column,
            ["operator"] = filter.Operator.ToString(),
            ["values"] = filter.Values
        };
    }

    private static List<Dictionary<string, object?>> ExtractAggregatedSample(EChartsOption option, int maxPoints)
    {
        var rows = new List<Dictionary<string, object?>>();
        var seriesList = option.Series ?? new List<Dictionary<string, object>>();

        foreach (var series in seriesList)
        {
            var seriesName = series.TryGetValue("name", out var nameValue)
                ? $"{nameValue ?? "series"}"
                : "series";

            if (!series.TryGetValue("data", out var dataValue) || dataValue is not System.Collections.IEnumerable enumerable)
            {
                continue;
            }

            foreach (var item in enumerable)
            {
                if (TryExtractSamplePoint(item, out var point))
                {
                    point["series"] = seriesName;
                    rows.Add(point);
                }
            }
        }

        return Downsample(rows, maxPoints);
    }

    private static bool TryExtractSamplePoint(object? item, out Dictionary<string, object?> point)
    {
        point = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (item == null)
        {
            return false;
        }

        if (item is object[] pair && pair.Length >= 2)
        {
            point["x"] = NormalizeValue(pair[0]);
            point["y"] = NormalizeValue(pair[1]);
            return true;
        }

        if (item is System.Collections.IList list && list.Count >= 2)
        {
            point["x"] = NormalizeValue(list[0]);
            point["y"] = NormalizeValue(list[1]);
            return true;
        }

        if (item is IDictionary<string, object> dictionary)
        {
            if (dictionary.TryGetValue("value", out var valueObj))
            {
                if (valueObj is object[] valuePair && valuePair.Length >= 2)
                {
                    point["x"] = NormalizeValue(valuePair[0]);
                    point["y"] = NormalizeValue(valuePair[1]);
                    return true;
                }

                if (valueObj is System.Collections.IList valueList && valueList.Count >= 2)
                {
                    point["x"] = NormalizeValue(valueList[0]);
                    point["y"] = NormalizeValue(valueList[1]);
                    return true;
                }

                point["x"] = null;
                point["y"] = NormalizeValue(valueObj);
                return true;
            }
        }

        point["x"] = null;
        point["y"] = NormalizeValue(item);
        return true;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is string text && text.Length > 200)
        {
            return text[..200];
        }

        return value;
    }

    private void EnforceBudget(LLMContextPayload payload, int maxContextBytes)
    {
        maxContextBytes = Math.Max(4_096, maxContextBytes);
        var context = payload.ContextObjects;
        var truncated = false;

        var bytes = EstimateBytes(context);
        if (bytes > maxContextBytes && context.TryGetValue("sampleSeries", out var sampleObject) &&
            sampleObject is List<Dictionary<string, object?>> samples && samples.Count > 10)
        {
            context["sampleSeries"] = Downsample(samples, 10);
            truncated = true;
            bytes = EstimateBytes(context);
        }

        if (bytes > maxContextBytes && context.TryGetValue("profiles", out var profilesObject) &&
            profilesObject is List<Dictionary<string, object?>> profiles && profiles.Count > 30)
        {
            context["profiles"] = profiles.Take(30).ToList();
            truncated = true;
            bytes = EstimateBytes(context);
        }

        if (bytes > maxContextBytes && context.TryGetValue("schema", out var schemaObject) &&
            schemaObject is List<Dictionary<string, object?>> schema && schema.Count > 50)
        {
            context["schema"] = schema.Take(50).ToList();
            truncated = true;
            bytes = EstimateBytes(context);
        }

        if (bytes > maxContextBytes)
        {
            _logger.LogWarning(
                "LLM context remains above budget after trimming: {SerializedBytes} bytes (limit {Limit}).",
                bytes,
                maxContextBytes);
            truncated = true;
        }

        payload.SerializedBytes = bytes;
        payload.Truncated = truncated;
    }

    private static int EstimateBytes(Dictionary<string, object?> context)
    {
        var json = JsonSerializer.Serialize(context, SerializerOptions);
        return Encoding.UTF8.GetByteCount(json);
    }

    private static List<T> Downsample<T>(IReadOnlyList<T> source, int maxItems)
    {
        if (source.Count <= maxItems || maxItems <= 0)
        {
            return source.ToList();
        }

        if (maxItems == 1)
        {
            return [source[0]];
        }

        var sampled = new List<T>(maxItems);
        var step = (source.Count - 1d) / (maxItems - 1d);
        for (var i = 0; i < maxItems; i++)
        {
            var index = (int)Math.Round(i * step);
            index = Math.Max(0, Math.Min(source.Count - 1, index));
            sampled.Add(source[index]);
        }

        return sampled;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
