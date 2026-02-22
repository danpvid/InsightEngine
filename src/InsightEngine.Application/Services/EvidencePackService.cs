using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Helpers;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.ImportSchema;
using InsightEngine.Domain.Models.Insights;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using InsightEngine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Application.Services;

public class EvidencePackService : IEvidencePackService
{
    private static readonly ConcurrentDictionary<string, CachedEvidencePack> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDataSetApplicationService _dataSetApplicationService;
    private readonly ILLMRedactionService _redactionService;
    private readonly IOptionsMonitor<LLMSettings> _llmSettingsMonitor;
    private readonly IOptionsMonitor<InsightEngineSettings> _runtimeSettingsMonitor;
    private readonly ILogger<EvidencePackService> _logger;

    public EvidencePackService(
        IDataSetApplicationService dataSetApplicationService,
        ILLMRedactionService redactionService,
        IOptionsMonitor<LLMSettings> llmSettingsMonitor,
        IOptionsMonitor<InsightEngineSettings> runtimeSettingsMonitor,
        ILogger<EvidencePackService> logger)
    {
        _dataSetApplicationService = dataSetApplicationService;
        _redactionService = redactionService;
        _llmSettingsMonitor = llmSettingsMonitor;
        _runtimeSettingsMonitor = runtimeSettingsMonitor;
        _logger = logger;
    }

    public async Task<Result<EvidencePackResult>> BuildEvidencePackAsync(
        DeepInsightsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DatasetId == Guid.Empty || string.IsNullOrWhiteSpace(request.RecommendationId))
        {
            return Result.Failure<EvidencePackResult>("DatasetId and RecommendationId are required.");
        }

        var profileResult = await _dataSetApplicationService.GetProfileAsync(request.DatasetId, cancellationToken);
        if (!profileResult.IsSuccess || profileResult.Data == null)
        {
            return Result.Failure<EvidencePackResult>(profileResult.Errors);
        }

        var schemaResult = await _dataSetApplicationService.GetSchemaAsync(request.DatasetId, cancellationToken);
        DatasetImportSchema? schema = schemaResult.IsSuccess ? schemaResult.Data : null;

        var indexResult = await _dataSetApplicationService.GetIndexAsync(request.DatasetId, cancellationToken);
        DatasetIndex? index = indexResult.IsSuccess ? indexResult.Data : null;

        var recommendationsResult = await _dataSetApplicationService.GetRecommendationsAsync(request.DatasetId, cancellationToken);
        if (!recommendationsResult.IsSuccess || recommendationsResult.Data == null)
        {
            return Result.Failure<EvidencePackResult>(recommendationsResult.Errors);
        }

        var recommendation = recommendationsResult.Data
            .FirstOrDefault(item => string.Equals(item.Id, request.RecommendationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation == null)
        {
            return Result.Failure<EvidencePackResult>($"Recommendation '{request.RecommendationId}' was not found.");
        }

        var chartResult = await _dataSetApplicationService.GetChartAsync(
            request.DatasetId,
            request.RecommendationId,
            request.Aggregation,
            request.TimeBin,
            request.MetricY,
            request.GroupBy,
            request.Filters,
            cancellationToken: cancellationToken);

        if (!chartResult.IsSuccess || chartResult.Data == null)
        {
            return Result.Failure<EvidencePackResult>(chartResult.Errors);
        }

        var chartResponse = chartResult.Data;
        var deepSettings = _llmSettingsMonitor.CurrentValue.DeepInsights;
        var queryHash = string.IsNullOrWhiteSpace(chartResponse.QueryHash)
            ? QueryHashHelper.ComputeQueryHash(recommendation, request.DatasetId)
            : chartResponse.QueryHash;
        var scenarioHash = request.Scenario == null ? "none" : QueryHashHelper.ComputeScenarioQueryHash(request.Scenario, request.DatasetId);
        var evidenceVersion = string.IsNullOrWhiteSpace(deepSettings.EvidenceVersion) ? "v1" : deepSettings.EvidenceVersion.Trim();
        var horizon = ResolveHorizon(request.Horizon, request.TimeBin ?? recommendation.Query.X.Bin?.ToString(), deepSettings);
        var cacheKey = string.Join("|",
            request.DatasetId,
            request.RecommendationId.Trim().ToLowerInvariant(),
            queryHash,
            scenarioHash,
            evidenceVersion,
            horizon,
            request.SensitiveMode ? "sensitive" : "default");

        var cacheTtl = TimeSpan.FromSeconds(Math.Max(10, _runtimeSettingsMonitor.CurrentValue.CacheTtlSeconds));
        if (TryReadCache(cacheKey, cacheTtl, out var cachedPack))
        {
            return Result.Success(new EvidencePackResult
            {
                EvidencePack = Clone(cachedPack!),
                CacheHit = true
            });
        }

        var maxPoints = Math.Max(100, deepSettings.MaxEvidenceSeriesPoints);
        var points = ExtractSeriesPoints(chartResponse.ExecutionResult.Option, maxPoints);
        var primaryMetric = string.IsNullOrWhiteSpace(request.MetricY)
            ? recommendation.Query.Y.Column
            : request.MetricY!.Trim();
        var yValues = points.Select(point => point.Y).ToList();
        var distribution = BuildDistribution(primaryMetric, yValues);
        var timeSeriesStats = BuildTimeSeriesStats(points);
        var segmentBreakdowns = BuildSegmentBreakdowns(points, deepSettings.MaxBreakdownSegments, request.SensitiveMode);
        var forecastPack = BuildForecastPack(points, horizon, request.TimeBin ?? recommendation.Query.X.Bin?.ToString(), deepSettings);

        var whatIfPack = await BuildWhatIfConclusionAsync(request, cancellationToken);
        var quality = BuildDatasetQuality(profileResult.Data, points, distribution);
        var sample = request.SensitiveMode
            ? new List<AggregatedSamplePoint>()
            : points.Select(point => new AggregatedSamplePoint
            {
                Series = point.Series,
                X = point.X,
                Y = point.Y
            }).ToList();

        var pack = new EvidencePack
        {
            EvidenceVersion = evidenceVersion,
            PackVersion = "2.0",
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            QueryHash = queryHash,
            DatasetQuality = quality,
            DistributionStats = distribution.Count > 0 ? [distribution] : new List<DistributionStatsEvidence>(),
            TimeSeriesStats = timeSeriesStats,
            SegmentBreakdowns = segmentBreakdowns,
            ForecastPack = forecastPack,
            WhatIfConclusionPack = whatIfPack,
            AggregatedSample = sample
        };

        pack.InsightPackV2 = BuildInsightPackV2(
            request,
            profileResult.Data,
            schema,
            index,
            recommendation,
            chartResponse,
            points,
            distribution,
            timeSeriesStats,
            segmentBreakdowns,
            pack.DatasetQuality);

        pack.Facts = BuildEvidenceFacts(pack);
        EnforceBudget(pack);

        Cache[cacheKey] = new CachedEvidencePack(DateTime.UtcNow, Clone(pack));

        _logger.LogInformation(
            "EvidencePack generated DatasetId={DatasetId} RecommendationId={RecommendationId} QueryHash={QueryHash} FactCount={FactCount} SerializedBytes={SerializedBytes}",
            request.DatasetId,
            request.RecommendationId,
            queryHash,
            pack.Facts.Count,
            pack.SerializedBytes);

        return Result.Success(new EvidencePackResult
        {
            EvidencePack = pack,
            CacheHit = false
        });
    }

    private async Task<WhatIfConclusionPack?> BuildWhatIfConclusionAsync(
        DeepInsightsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Scenario == null)
        {
            return null;
        }

        var simulationResult = await _dataSetApplicationService.SimulateAsync(request.DatasetId, request.Scenario, cancellationToken);
        if (!simulationResult.IsSuccess || simulationResult.Data == null)
        {
            _logger.LogWarning(
                "Scenario simulation failed while building evidence pack DatasetId={DatasetId} RecommendationId={RecommendationId}.",
                request.DatasetId,
                request.RecommendationId);
            return null;
        }

        var response = simulationResult.Data;
        var baseline = response.BaselineSeries.Select(item => item.Value).ToList();
        var simulated = response.SimulatedSeries.Select(item => item.Value).ToList();
        var deltas = response.DeltaSeries.Select(item => item.Delta).ToList();
        if (baseline.Count == 0 || simulated.Count == 0 || deltas.Count == 0)
        {
            return null;
        }

        var baselineTrend = ComputeSlope(baseline);
        var simulatedTrend = ComputeSlope(simulated);
        var baselineStdDev = ComputeStdDev(baseline);
        var simulatedStdDev = ComputeStdDev(simulated);

        return new WhatIfConclusionPack
        {
            DeltaMean = Math.Round(deltas.Average(), 6),
            DeltaSum = Math.Round(deltas.Sum(), 6),
            DeltaMax = Math.Round(deltas.Max(), 6),
            DeltaVolatility = Math.Round(simulatedStdDev - baselineStdDev, 6),
            DeltaTrendSlope = Math.Round(simulatedTrend - baselineTrend, 6),
            TopDrivers = request.Scenario.Operations
                .Select((operation, index) => $"{index + 1}. {DescribeOperation(operation)}")
                .Take(3)
                .ToList()
        };
    }

    private static DatasetQualityEvidence BuildDatasetQuality(
        DatasetProfile profile,
        IReadOnlyList<SeriesPoint> points,
        DistributionStatsEvidence distribution)
    {
        var columns = profile.Columns ?? new List<ColumnProfile>();
        var rowCount = Math.Max(profile.RowCount, 0);
        var missingAverage = columns.Count == 0 ? 0 : columns.Average(column => column.NullRate);
        var maxMissing = columns.Count == 0 ? 0 : columns.Max(column => column.NullRate);
        var maxMissingColumn = columns
            .OrderByDescending(column => column.NullRate)
            .Select(column => column.Name)
            .FirstOrDefault() ?? string.Empty;
        var maxDistinct = columns.Count == 0 ? 0 : columns.Max(column => column.DistinctCount);
        var duplicateRate = rowCount <= 0
            ? 0
            : Math.Clamp(1d - (maxDistinct / (double)rowCount), 0d, 1d);

        var timePoints = points.Where(point => point.Date != null).Select(point => point.Date!.Value).OrderBy(value => value).ToList();
        var coverage = timePoints.Count >= 2
            ? $"{timePoints.First():yyyy-MM-dd}..{timePoints.Last():yyyy-MM-dd}"
            : "unknown";

        var invalidDateCount = points.Count(point => point.LooksLikeDate && point.Date == null);
        var impossibleValueCount = points.Count(point => point.Y < 0);
        var extremeSkew = Math.Abs(distribution.SkewnessProxy) >= 1.5;

        return new DatasetQualityEvidence
        {
            RowCount = rowCount,
            ColumnCount = columns.Count,
            MissingRateAverage = Math.Round(missingAverage, 6),
            MissingRateMax = Math.Round(maxMissing, 6),
            MissingRateMaxColumn = maxMissingColumn,
            DuplicateRate = Math.Round(duplicateRate, 6),
            TimestampCoverage = coverage,
            InvalidDateCount = invalidDateCount,
            ImpossibleValueCount = impossibleValueCount,
            ExtremeSkewDetected = extremeSkew
        };
    }

    private static DistributionStatsEvidence BuildDistribution(string metric, IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return new DistributionStatsEvidence
            {
                Metric = metric
            };
        }

        var ordered = values.OrderBy(value => value).ToList();
        var mean = values.Average();
        var stdDev = ComputeStdDev(values);
        var median = Percentile(ordered, 0.5);
        var p05 = Percentile(ordered, 0.05);
        var p25 = Percentile(ordered, 0.25);
        var p75 = Percentile(ordered, 0.75);
        var p95 = Percentile(ordered, 0.95);
        var iqr = p75 - p25;
        var cv = Math.Abs(mean) < 1e-9 ? 0 : stdDev / Math.Abs(mean);
        var skewness = stdDev < 1e-9 ? 0 : (mean - median) / stdDev;

        return new DistributionStatsEvidence
        {
            Metric = metric,
            Count = values.Count,
            Mean = Math.Round(mean, 6),
            Median = Math.Round(median, 6),
            StdDev = Math.Round(stdDev, 6),
            Min = Math.Round(ordered.First(), 6),
            Max = Math.Round(ordered.Last(), 6),
            P05 = Math.Round(p05, 6),
            P25 = Math.Round(p25, 6),
            P75 = Math.Round(p75, 6),
            P95 = Math.Round(p95, 6),
            Iqr = Math.Round(iqr, 6),
            CoefficientOfVariation = Math.Round(cv, 6),
            SkewnessProxy = Math.Round(skewness, 6)
        };
    }

    private static TimeSeriesStatsEvidence? BuildTimeSeriesStats(IReadOnlyList<SeriesPoint> points)
    {
        var timeSeries = points
            .Where(point => point.Date != null)
            .OrderBy(point => point.Date)
            .ToList();
        if (timeSeries.Count < 6)
        {
            return null;
        }

        var values = timeSeries.Select(point => point.Y).ToList();
        var mean = values.Average();
        var stdDev = ComputeStdDev(values);
        var slope = ComputeSlope(values);
        var normalizedSlope = Math.Abs(mean) < 1e-9 ? 0 : slope / Math.Abs(mean);
        var trendClassification = normalizedSlope switch
        {
            > 0.01 => "Rising",
            < -0.01 => "Falling",
            _ => "Flat"
        };

        var volatilityRatio = Math.Abs(mean) < 1e-9 ? 0 : stdDev / Math.Abs(mean);
        var weeklyStrength = ComputePatternStrength(timeSeries, point => ((int)point.Date!.Value.DayOfWeek).ToString(CultureInfo.InvariantCulture));
        var monthlyStrength = ComputePatternStrength(timeSeries, point => point.Date!.Value.Month.ToString(CultureInfo.InvariantCulture));
        var changePoints = DetectChangePoints(timeSeries);

        return new TimeSeriesStatsEvidence
        {
            PointCount = timeSeries.Count,
            TrendSlope = Math.Round(slope, 6),
            TrendClassification = trendClassification,
            VolatilityRatio = Math.Round(volatilityRatio, 6),
            VolatilityBandLow = Math.Round(mean - stdDev, 6),
            VolatilityBandHigh = Math.Round(mean + stdDev, 6),
            WeeklyPatternStrength = Math.Round(weeklyStrength, 6),
            MonthlyPatternStrength = Math.Round(monthlyStrength, 6),
            ChangePoints = changePoints
        };
    }

    private static List<SegmentBreakdownEvidence> BuildSegmentBreakdowns(
        IReadOnlyList<SeriesPoint> points,
        int maxSegments,
        bool sensitiveMode)
    {
        if (points.Count == 0)
        {
            return new List<SegmentBreakdownEvidence>();
        }

        maxSegments = Math.Clamp(maxSegments, 3, 20);
        var grouped = points
            .GroupBy(point => string.IsNullOrWhiteSpace(point.Series) ? "series" : point.Series)
            .Select(group =>
            {
                var values = group.Select(point => point.Y).ToList();
                var contribution = values.Sum();
                var cv = Math.Abs(values.Average()) < 1e-9 ? 0 : ComputeStdDev(values) / Math.Abs(values.Average());
                var segmentName = sensitiveMode ? "segment" : group.Key;
                return new SegmentBreakdownEvidence
                {
                    Segment = segmentName,
                    ContributionValue = Math.Round(contribution, 6),
                    StabilityScore = Math.Round(1d / (1d + Math.Max(0, cv)), 6)
                };
            })
            .OrderByDescending(item => Math.Abs(item.ContributionValue))
            .Take(maxSegments)
            .ToList();

        var total = grouped.Sum(item => Math.Abs(item.ContributionValue));
        if (total <= 0)
        {
            foreach (var item in grouped)
            {
                item.SharePercent = 0;
            }
        }
        else
        {
            foreach (var item in grouped)
            {
                item.SharePercent = Math.Round((Math.Abs(item.ContributionValue) / total) * 100d, 4);
            }
        }

        var shares = grouped.Select(item => item.SharePercent).ToList();
        var meanShare = shares.Count == 0 ? 0 : shares.Average();
        var stdShare = ComputeStdDev(shares);
        foreach (var item in grouped)
        {
            item.IsOutlierSegment = stdShare > 0 && Math.Abs(item.SharePercent - meanShare) > (2.0 * stdShare);
        }

        return grouped;
    }

    private static ForecastPack BuildForecastPack(
        IReadOnlyList<SeriesPoint> points,
        int horizon,
        string? timeBin,
        DeepInsightsSettings settings)
    {
        var source = points
            .OrderBy(point => point.Date ?? DateTime.MinValue.AddTicks(point.Index))
            .Select(point => point.Y)
            .ToList();

        if (source.Count < 3)
        {
            return new ForecastPack
            {
                Horizon = horizon,
                Label = "baseline projection"
            };
        }

        var methods = new List<ForecastMethodEvidence>
        {
            BuildNaiveForecast(source, horizon, points, timeBin),
            BuildMovingAverageForecast(source, horizon, points, timeBin, settings.ForecastMovingAverageWindow),
            BuildLinearRegressionForecast(source, horizon, points, timeBin)
        };

        return new ForecastPack
        {
            Horizon = horizon,
            Label = "baseline projection",
            Methods = methods
        };
    }

    private static ForecastMethodEvidence BuildNaiveForecast(
        IReadOnlyList<double> source,
        int horizon,
        IReadOnlyList<SeriesPoint> points,
        string? timeBin)
    {
        var last = source[^1];
        var residuals = new List<double>();
        for (var i = 1; i < source.Count; i++)
        {
            residuals.Add(source[i] - source[i - 1]);
        }

        var std = ComputeStdDev(residuals);
        var rmse = Math.Sqrt(residuals.Select(value => value * value).DefaultIfEmpty(0).Average());
        var items = BuildForecastPoints(
            points,
            timeBin,
            horizon,
            _ => last,
            std);

        return new ForecastMethodEvidence
        {
            Method = "naive",
            ResidualStdDev = Math.Round(std, 6),
            Rmse = Math.Round(rmse, 6),
            Points = items
        };
    }

    private static ForecastMethodEvidence BuildMovingAverageForecast(
        IReadOnlyList<double> source,
        int horizon,
        IReadOnlyList<SeriesPoint> points,
        string? timeBin,
        int window)
    {
        window = Math.Clamp(window, 2, Math.Max(2, Math.Min(15, source.Count - 1)));
        var residuals = new List<double>();
        for (var i = window; i < source.Count; i++)
        {
            var predicted = source.Skip(i - window).Take(window).Average();
            residuals.Add(source[i] - predicted);
        }

        var history = source.ToList();
        var generated = new List<double>();
        for (var i = 0; i < horizon; i++)
        {
            var predicted = history.Skip(history.Count - window).Take(window).Average();
            generated.Add(predicted);
            history.Add(predicted);
        }

        var std = ComputeStdDev(residuals);
        var rmse = Math.Sqrt(residuals.Select(value => value * value).DefaultIfEmpty(0).Average());
        var items = BuildForecastPoints(
            points,
            timeBin,
            horizon,
            index => generated[index],
            std);

        return new ForecastMethodEvidence
        {
            Method = "movingAverage",
            ResidualStdDev = Math.Round(std, 6),
            Rmse = Math.Round(rmse, 6),
            Points = items
        };
    }

    private static ForecastMethodEvidence BuildLinearRegressionForecast(
        IReadOnlyList<double> source,
        int horizon,
        IReadOnlyList<SeriesPoint> points,
        string? timeBin)
    {
        var count = source.Count;
        var xs = Enumerable.Range(0, count).Select(value => (double)value).ToList();
        var meanX = xs.Average();
        var meanY = source.Average();
        var numerator = 0d;
        var denominator = 0d;
        for (var i = 0; i < count; i++)
        {
            var centeredX = xs[i] - meanX;
            numerator += centeredX * (source[i] - meanY);
            denominator += centeredX * centeredX;
        }

        var slope = Math.Abs(denominator) < 1e-9 ? 0 : numerator / denominator;
        var intercept = meanY - (slope * meanX);
        var residuals = xs
            .Select((x, index) => source[index] - (intercept + (slope * x)))
            .ToList();

        var std = ComputeStdDev(residuals);
        var rmse = Math.Sqrt(residuals.Select(value => value * value).DefaultIfEmpty(0).Average());
        var items = BuildForecastPoints(
            points,
            timeBin,
            horizon,
            index =>
            {
                var x = count + index;
                return intercept + (slope * x);
            },
            std);

        return new ForecastMethodEvidence
        {
            Method = "linearRegression",
            ResidualStdDev = Math.Round(std, 6),
            Rmse = Math.Round(rmse, 6),
            Points = items
        };
    }

    private static List<ForecastPointEvidence> BuildForecastPoints(
        IReadOnlyList<SeriesPoint> sourcePoints,
        string? timeBin,
        int horizon,
        Func<int, double> valueFactory,
        double residualStdDev)
    {
        var points = new List<ForecastPointEvidence>(horizon);
        var baseDate = sourcePoints
            .Where(point => point.Date != null)
            .Select(point => point.Date!.Value)
            .DefaultIfEmpty(DateTime.UtcNow.Date)
            .Max();

        for (var i = 0; i < horizon; i++)
        {
            var projectedValue = valueFactory(i);
            var band = residualStdDev * 1.64;
            var date = NextDate(baseDate, timeBin, i + 1);
            var position = sourcePoints.Any(point => point.Date != null)
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : $"t+{i + 1}";

            points.Add(new ForecastPointEvidence
            {
                Position = position,
                Value = Math.Round(projectedValue, 6),
                LowerBand = Math.Round(projectedValue - band, 6),
                UpperBand = Math.Round(projectedValue + band, 6)
            });
        }

        return points;
    }

    private static List<EvidenceFact> BuildEvidenceFacts(EvidencePack pack)
    {
        var facts = new List<EvidenceFact>();
        AddFact(facts, "DQ_ROW_COUNT", "Dataset rows", pack.DatasetQuality.RowCount.ToString(CultureInfo.InvariantCulture));
        AddFact(facts, "DQ_COLUMN_COUNT", "Dataset columns", pack.DatasetQuality.ColumnCount.ToString(CultureInfo.InvariantCulture));
        AddFact(facts, "DQ_MISSING_RATE_AVG", "Average missing rate", FormatNumber(pack.DatasetQuality.MissingRateAverage));
        AddFact(facts, "DQ_MISSING_RATE_MAX", $"Max missing rate ({pack.DatasetQuality.MissingRateMaxColumn})", FormatNumber(pack.DatasetQuality.MissingRateMax));
        AddFact(facts, "DQ_DUPLICATE_RATE_EST", "Estimated duplicate rate", FormatNumber(pack.DatasetQuality.DuplicateRate));
        AddFact(facts, "DQ_TIMESTAMP_COVERAGE", "Timestamp coverage", pack.DatasetQuality.TimestampCoverage);
        AddFact(facts, "DQ_INVALID_DATE_COUNT", "Invalid date count", pack.DatasetQuality.InvalidDateCount.ToString(CultureInfo.InvariantCulture));
        AddFact(facts, "DQ_IMPOSSIBLE_VALUE_COUNT", "Impossible value count", pack.DatasetQuality.ImpossibleValueCount.ToString(CultureInfo.InvariantCulture));

        var distribution = pack.DistributionStats.FirstOrDefault();
        if (distribution != null && distribution.Count > 0)
        {
            var metricSuffix = NormalizeId(distribution.Metric);
            AddFact(facts, $"DIST_COUNT_{metricSuffix}", $"Distribution count for {distribution.Metric}", distribution.Count.ToString(CultureInfo.InvariantCulture));
            AddFact(facts, $"DIST_MEAN_{metricSuffix}", $"Mean of {distribution.Metric}", FormatNumber(distribution.Mean));
            AddFact(facts, $"DIST_MEDIAN_{metricSuffix}", $"Median of {distribution.Metric}", FormatNumber(distribution.Median));
            AddFact(facts, $"DIST_STDDEV_{metricSuffix}", $"StdDev of {distribution.Metric}", FormatNumber(distribution.StdDev));
            AddFact(facts, $"DIST_MIN_{metricSuffix}", $"Min of {distribution.Metric}", FormatNumber(distribution.Min));
            AddFact(facts, $"DIST_MAX_{metricSuffix}", $"Max of {distribution.Metric}", FormatNumber(distribution.Max));
            AddFact(facts, $"DIST_P05_{metricSuffix}", $"P05 of {distribution.Metric}", FormatNumber(distribution.P05));
            AddFact(facts, $"DIST_P95_{metricSuffix}", $"P95 of {distribution.Metric}", FormatNumber(distribution.P95));
            AddFact(facts, $"DIST_IQR_{metricSuffix}", $"IQR of {distribution.Metric}", FormatNumber(distribution.Iqr));
            AddFact(facts, $"DIST_CV_{metricSuffix}", $"Coefficient of variation for {distribution.Metric}", FormatNumber(distribution.CoefficientOfVariation));
            AddFact(facts, $"DIST_SKEW_PROXY_{metricSuffix}", $"Skewness proxy for {distribution.Metric}", FormatNumber(distribution.SkewnessProxy));
        }

        if (pack.TimeSeriesStats != null)
        {
            AddFact(facts, "TS_POINT_COUNT", "Time-series points", pack.TimeSeriesStats.PointCount.ToString(CultureInfo.InvariantCulture));
            AddFact(facts, "TS_TREND_SLOPE", "Trend slope", FormatNumber(pack.TimeSeriesStats.TrendSlope));
            AddFact(facts, "TS_TREND_CLASS", "Trend classification", pack.TimeSeriesStats.TrendClassification);
            AddFact(facts, "TS_VOLATILITY_RATIO", "Volatility ratio", FormatNumber(pack.TimeSeriesStats.VolatilityRatio));
            AddFact(facts, "TS_WEEKDAY_PATTERN", "Weekday pattern strength", FormatNumber(pack.TimeSeriesStats.WeeklyPatternStrength));
            AddFact(facts, "TS_MONTH_PATTERN", "Month pattern strength", FormatNumber(pack.TimeSeriesStats.MonthlyPatternStrength));

            for (var index = 0; index < pack.TimeSeriesStats.ChangePoints.Count; index++)
            {
                var item = pack.TimeSeriesStats.ChangePoints[index];
                AddFact(facts, $"TS_CHANGEPOINT_{index + 1}", $"Change point at {item.Position}", FormatNumber(item.ShiftMagnitude));
            }
        }

        for (var index = 0; index < pack.SegmentBreakdowns.Count; index++)
        {
            var segment = pack.SegmentBreakdowns[index];
            AddFact(facts, $"SEG_SHARE_{index + 1}", $"Segment share {segment.Segment}", FormatNumber(segment.SharePercent));
            AddFact(facts, $"SEG_STABILITY_{index + 1}", $"Segment stability {segment.Segment}", FormatNumber(segment.StabilityScore));
        }

        AddFact(facts, "FORECAST_HORIZON", "Forecast horizon", pack.ForecastPack.Horizon.ToString(CultureInfo.InvariantCulture));
        foreach (var method in pack.ForecastPack.Methods)
        {
            var methodId = NormalizeId(method.Method);
            AddFact(facts, $"FORECAST_{methodId}_RMSE", $"RMSE for {method.Method}", FormatNumber(method.Rmse));
            AddFact(facts, $"FORECAST_{methodId}_RES_STD", $"Residual stddev for {method.Method}", FormatNumber(method.ResidualStdDev));
            if (method.Points.Count > 0)
            {
                AddFact(facts, $"FORECAST_{methodId}_FIRST", $"First forecast point ({method.Method})", FormatNumber(method.Points[0].Value));
                AddFact(facts, $"FORECAST_{methodId}_LAST", $"Last forecast point ({method.Method})", FormatNumber(method.Points[^1].Value));
            }
        }

        if (pack.WhatIfConclusionPack != null)
        {
            AddFact(facts, "SCN_DELTA_MEAN", "Scenario delta mean", FormatNumber(pack.WhatIfConclusionPack.DeltaMean));
            AddFact(facts, "SCN_DELTA_SUM", "Scenario delta sum", FormatNumber(pack.WhatIfConclusionPack.DeltaSum));
            AddFact(facts, "SCN_DELTA_MAX", "Scenario delta max", FormatNumber(pack.WhatIfConclusionPack.DeltaMax));
            AddFact(facts, "SCN_DELTA_VOLATILITY", "Scenario delta volatility", FormatNumber(pack.WhatIfConclusionPack.DeltaVolatility));
            AddFact(facts, "SCN_DELTA_TREND", "Scenario delta trend slope", FormatNumber(pack.WhatIfConclusionPack.DeltaTrendSlope));
        }

        return facts;
    }

    private void EnforceBudget(EvidencePack pack)
    {
        var maxContextBytes = Math.Max(4_096, _llmSettingsMonitor.CurrentValue.MaxContextBytes);
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["evidencePack"] = pack
        };

        var redacted = _redactionService.RedactContext(context);
        var serializedBytes = EstimateBytes(redacted);
        var truncated = false;

        if (serializedBytes > maxContextBytes && pack.AggregatedSample.Count > 0)
        {
            pack.AggregatedSample = new List<AggregatedSamplePoint>();
            truncated = true;

            redacted = _redactionService.RedactContext(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["evidencePack"] = pack
            });
            serializedBytes = EstimateBytes(redacted);
        }

        if (serializedBytes > maxContextBytes && pack.SegmentBreakdowns.Count > 5)
        {
            pack.SegmentBreakdowns = pack.SegmentBreakdowns.Take(5).ToList();
            truncated = true;

            redacted = _redactionService.RedactContext(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["evidencePack"] = pack
            });
            serializedBytes = EstimateBytes(redacted);
        }

        pack.SerializedBytes = serializedBytes;
        pack.Truncated = truncated || serializedBytes > maxContextBytes;
    }

    private static int ResolveHorizon(int? requestedHorizon, string? timeBin, DeepInsightsSettings settings)
    {
        var defaultHorizon = Math.Max(7, settings.ForecastDefaultHorizon);
        if (string.Equals(timeBin, "Day", StringComparison.OrdinalIgnoreCase))
        {
            defaultHorizon = Math.Min(defaultHorizon, 30);
        }

        var horizon = requestedHorizon ?? defaultHorizon;
        return Math.Clamp(horizon, 7, Math.Max(7, settings.ForecastMaxHorizon));
    }

    private static bool TryReadCache(string cacheKey, TimeSpan ttl, out EvidencePack? cachedPack)
    {
        cachedPack = null;
        if (!Cache.TryGetValue(cacheKey, out var entry))
        {
            return false;
        }

        if (DateTime.UtcNow - entry.StoredAtUtc > ttl)
        {
            Cache.TryRemove(cacheKey, out _);
            return false;
        }

        cachedPack = entry.Pack;
        return true;
    }

    private static List<SeriesPoint> ExtractSeriesPoints(EChartsOption option, int maxPoints)
    {
        var output = new List<SeriesPoint>();
        var seriesList = option.Series ?? new List<Dictionary<string, object>>();

        foreach (var series in seriesList)
        {
            var seriesName = series.TryGetValue("name", out var name) ? $"{name ?? "series"}" : "series";
            if (!series.TryGetValue("data", out var dataObj) || dataObj is not System.Collections.IEnumerable dataItems)
            {
                continue;
            }

            var index = 0L;
            foreach (var item in dataItems)
            {
                if (TryExtractPoint(item, seriesName, index, out var point))
                {
                    output.Add(point);
                }

                index++;
            }
        }

        return Downsample(output, maxPoints);
    }

    private static bool TryExtractPoint(object? item, string seriesName, long index, out SeriesPoint point)
    {
        point = default!;
        if (item == null)
        {
            return false;
        }

        string xValue;
        double yValue;

        if (item is object[] pair && pair.Length >= 2)
        {
            if (!TryGetDouble(pair[1], out yValue))
            {
                return false;
            }

            xValue = NormalizeX(pair[0], index);
            point = CreatePoint(seriesName, xValue, yValue, index);
            return true;
        }

        if (item is System.Collections.IList list && list.Count >= 2)
        {
            if (!TryGetDouble(list[1], out yValue))
            {
                return false;
            }

            xValue = NormalizeX(list[0], index);
            point = CreatePoint(seriesName, xValue, yValue, index);
            return true;
        }

        if (item is IDictionary<string, object> obj &&
            obj.TryGetValue("value", out var valueObj) &&
            valueObj is System.Collections.IList valueList &&
            valueList.Count >= 2 &&
            TryGetDouble(valueList[1], out yValue))
        {
            xValue = NormalizeX(valueList[0], index);
            point = CreatePoint(seriesName, xValue, yValue, index);
            return true;
        }

        if (!TryGetDouble(item, out yValue))
        {
            return false;
        }

        xValue = $"idx_{index}";
        point = CreatePoint(seriesName, xValue, yValue, index);
        return true;
    }

    private static SeriesPoint CreatePoint(string series, string x, double y, long index)
    {
        var hasDate = TryParseDate(x, out var date);
        var looksLikeDate = MightBeDate(x);
        return new SeriesPoint(series, x, y, index, hasDate ? date : null, looksLikeDate);
    }

    private static List<ChangePointEvidence> DetectChangePoints(IReadOnlyList<SeriesPoint> points)
    {
        if (points.Count < 12)
        {
            return new List<ChangePointEvidence>();
        }

        var ordered = points
            .Where(point => point.Date != null)
            .OrderBy(point => point.Date)
            .ToList();
        if (ordered.Count < 12)
        {
            return new List<ChangePointEvidence>();
        }

        var values = ordered.Select(point => point.Y).ToList();
        var window = Math.Clamp(values.Count / 8, 3, 20);
        var scored = new List<(int Index, double Score)>();

        for (var i = window; i < values.Count - window; i++)
        {
            var left = values.Skip(i - window).Take(window).Average();
            var right = values.Skip(i).Take(window).Average();
            scored.Add((i, Math.Abs(right - left)));
        }

        return scored
            .OrderByDescending(item => item.Score)
            .Take(3)
            .Select(item => new ChangePointEvidence
            {
                Position = ordered[item.Index].Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ShiftMagnitude = Math.Round(item.Score, 6)
            })
            .ToList();
    }

    private static double ComputePatternStrength(
        IReadOnlyList<SeriesPoint> points,
        Func<SeriesPoint, string> bucketSelector)
    {
        if (points.Count < 8)
        {
            return 0;
        }

        var values = points.Select(point => point.Y).ToList();
        var baseStd = ComputeStdDev(values);
        if (baseStd < 1e-9)
        {
            return 0;
        }

        var means = points
            .GroupBy(bucketSelector)
            .Select(group => group.Average(point => point.Y))
            .ToList();
        if (means.Count <= 1)
        {
            return 0;
        }

        return ComputeStdDev(means) / baseStd;
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        percentile = Math.Clamp(percentile, 0, 1);
        var index = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = index - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
    }

    private static double ComputeStdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        var variance = values.Sum(value =>
        {
            var diff = value - mean;
            return diff * diff;
        }) / values.Count;

        return Math.Sqrt(Math.Max(0, variance));
    }

    private static double ComputeSlope(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var count = values.Count;
        var meanX = (count - 1) / 2d;
        var meanY = values.Average();
        var numerator = 0d;
        var denominator = 0d;

        for (var i = 0; i < count; i++)
        {
            var centeredX = i - meanX;
            numerator += centeredX * (values[i] - meanY);
            denominator += centeredX * centeredX;
        }

        return Math.Abs(denominator) < 1e-9 ? 0 : numerator / denominator;
    }

    private static DateTime NextDate(DateTime current, string? timeBin, int offset)
    {
        return timeBin?.Trim().ToLowerInvariant() switch
        {
            "day" => current.AddDays(offset),
            "week" => current.AddDays(7 * offset),
            "month" => current.AddMonths(offset),
            "quarter" => current.AddMonths(3 * offset),
            "year" => current.AddYears(offset),
            _ => current.AddDays(offset)
        };
    }

    private static bool TryGetDouble(object? value, out double numeric)
    {
        if (value is null)
        {
            numeric = 0;
            return false;
        }

        switch (value)
        {
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                numeric = d;
                return true;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                numeric = f;
                return true;
            case decimal m:
                numeric = (double)m;
                return true;
            case int i:
                numeric = i;
                return true;
            case long l:
                numeric = l;
                return true;
            case string s:
                {
                    var normalized = s.Replace(",", ".", StringComparison.Ordinal).Trim();
                    if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) &&
                        !double.IsNaN(parsed) &&
                        !double.IsInfinity(parsed))
                    {
                        numeric = parsed;
                        return true;
                    }

                    numeric = 0;
                    return false;
                }
            default:
                {
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text) &&
                        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) &&
                        !double.IsNaN(parsed) &&
                        !double.IsInfinity(parsed))
                    {
                        numeric = parsed;
                        return true;
                    }

                    numeric = 0;
                    return false;
                }
        }
    }

    private static string NormalizeX(object? rawValue, long fallbackIndex)
    {
        if (rawValue == null)
        {
            return $"idx_{fallbackIndex}";
        }

        if (rawValue is DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (rawValue is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (rawValue is long l && l > 100_000_000_000)
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime;
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (rawValue is int i && i > 10_000_000)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(i).UtcDateTime;
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var text = Convert.ToString(rawValue, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"idx_{fallbackIndex}";
        }

        if (text.Length > 120)
        {
            text = text[..120];
        }

        return text;
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        var formats = new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyyMMdd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fffZ" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        value = default;
        return false;
    }

    private static bool MightBeDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains('-', StringComparison.Ordinal) ||
               text.Contains('/', StringComparison.Ordinal) ||
               (text.Length == 8 && text.All(char.IsDigit));
    }

    private static string DescribeOperation(ScenarioOperation operation)
    {
        return operation.Type switch
        {
            ScenarioOperationType.MultiplyMetric => $"multiply metric by {operation.Factor?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?"}",
            ScenarioOperationType.AddConstant => $"add constant {operation.Constant?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?"}",
            ScenarioOperationType.Clamp => $"clamp between {operation.Min?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-inf"} and {operation.Max?.ToString("0.###", CultureInfo.InvariantCulture) ?? "+inf"}",
            ScenarioOperationType.RemoveCategory => $"remove category from {operation.Column ?? "dimension"}",
            ScenarioOperationType.FilterOut => $"filter out values from {operation.Column ?? "dimension"}",
            _ => operation.Type.ToString()
        };
    }

    private static int EstimateBytes(object value)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        return Encoding.UTF8.GetByteCount(json);
    }

    private static void AddFact(List<EvidenceFact> facts, string evidenceId, string claim, string value)
    {
        facts.Add(new EvidenceFact
        {
            EvidenceId = evidenceId,
            ShortClaim = claim,
            Value = value
        });
    }

    private static string NormalizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var chars = value
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        return new string(chars);
    }

    private static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static InsightPackV2 BuildInsightPackV2(
        DeepInsightsRequest request,
        DatasetProfile profile,
        DatasetImportSchema? schema,
        DatasetIndex? index,
        ChartRecommendation recommendation,
        ChartExecutionResponse chartResponse,
        IReadOnlyList<SeriesPoint> points,
        DistributionStatsEvidence distribution,
        TimeSeriesStatsEvidence? timeSeriesStats,
        List<SegmentBreakdownEvidence> segmentBreakdowns,
        DatasetQualityEvidence quality)
    {
        var schemaColumns = BuildSchemaColumns(profile, schema);
        var ignoredColumns = schema?.Columns.Where(c => c.IsIgnored).Select(c => c.Name).ToList()
            ?? profile.Columns.Where(c => c.IsIgnored).Select(c => c.Name).ToList();
        var targetColumn = schema?.TargetColumn ?? profile.TargetColumn;

        var v2 = new InsightPackV2
        {
            Version = "2.0",
            DatasetSummary = new InsightDatasetSummary
            {
                DatasetId = request.DatasetId,
                RowCount = profile.RowCount,
                ColumnCount = profile.Columns.Count,
                TimeRange = ResolveTimeRange(index),
                CurrencyCode = schema?.CurrencyCode,
                SamplingInfo = new InsightSamplingInfo
                {
                    SampleRowsUsed = profile.SampleSize,
                    SampleStrategy = "profile-sample"
                }
            },
            SchemaContext = new InsightSchemaContext
            {
                TargetColumn = targetColumn,
                IgnoredColumns = ignoredColumns,
                Columns = schemaColumns
            },
            DataQuality = BuildDataQualityV2(profile, schema, index, quality),
            Relationships = BuildRelationshipsV2(index, targetColumn),
            Actions = BuildActionsV2(segmentBreakdowns, timeSeriesStats),
            EvidenceIndex = BuildEvidenceAnchorsV2()
        };

        if (!string.IsNullOrWhiteSpace(targetColumn))
        {
            v2.TargetStory = BuildTargetStoryV2(
                targetColumn!,
                schemaColumns,
                points,
                timeSeriesStats,
                segmentBreakdowns,
                recommendation,
                chartResponse);
        }

        return v2;
    }

    private static List<InsightSchemaColumn> BuildSchemaColumns(DatasetProfile profile, DatasetImportSchema? schema)
    {
        if (schema?.Columns?.Count > 0)
        {
            return schema.Columns.Select(column =>
            {
                var profileCol = profile.Columns.FirstOrDefault(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase));
                var confirmed = (column.ConfirmedType == InferredType.Number ? InferredType.Decimal : column.ConfirmedType).ToString();
                return new InsightSchemaColumn
                {
                    Name = column.Name,
                    ConfirmedType = confirmed,
                    SemanticType = ResolveSemanticTypeLabel(column.ConfirmedType, column.Name),
                    RoleHint = ResolveRoleHint(column.ConfirmedType, column.Name),
                    PercentageScaleHint = column.PercentageScaleHint?.ToString(),
                    FormattingHints = BuildFormattingHints(column.ConfirmedType, column.CurrencyCode, profileCol)
                };
            }).ToList();
        }

        return profile.Columns.Select(column =>
        {
            var confirmed = (column.ConfirmedType ?? column.InferredType).NormalizeLegacy();
            return new InsightSchemaColumn
            {
                Name = column.Name,
                ConfirmedType = confirmed.ToString(),
                SemanticType = ResolveSemanticTypeLabel(confirmed, column.Name),
                RoleHint = ResolveRoleHint(confirmed, column.Name),
                PercentageScaleHint = column.PercentageScaleHint?.ToString(),
                FormattingHints = BuildFormattingHints(confirmed, column.CurrencyCode, column)
            };
        }).ToList();
    }

    private static InsightFormattingHints BuildFormattingHints(InferredType type, string? currencyCode, ColumnProfile? profile)
    {
        return new InsightFormattingHints
        {
            Decimals = type switch
            {
                InferredType.Integer => 0,
                InferredType.Percentage => 2,
                InferredType.Money => 2,
                _ => 3
            },
            CurrencySymbol = type == InferredType.Money
                ? (string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase) ? "$" : "R$")
                : null,
            Unit = type == InferredType.Percentage
                ? "%"
                : (profile?.Name.Contains("qty", StringComparison.OrdinalIgnoreCase) == true ? "qty" : null)
        };
    }

    private static InsightDataQualityContext BuildDataQualityV2(
        DatasetProfile profile,
        DatasetImportSchema? schema,
        DatasetIndex? index,
        DatasetQualityEvidence quality)
    {
        var warnings = new List<string>();
        warnings.AddRange(index?.Quality?.Warnings ?? []);
        if (quality.MissingRateMax >= 0.2)
        {
            warnings.Add($"High missingness detected in {quality.MissingRateMaxColumn} ({quality.MissingRateMax:P1}).");
        }

        if (quality.InvalidDateCount > 0)
        {
            warnings.Add($"Date parsing inconsistencies detected ({quality.InvalidDateCount} values).");
        }

        return new InsightDataQualityContext
        {
            NullHotspots = profile.Columns
                .OrderByDescending(column => column.NullRate)
                .Take(8)
                .Select(column => new InsightNullHotspot
                {
                    Column = column.Name,
                    NullRate = Math.Round(column.NullRate, 6)
                })
                .ToList(),
            DuplicateRateEstimate = Math.Round(index?.Quality?.DuplicateRowRate ?? quality.DuplicateRate, 6),
            IdLikeCandidates = profile.Columns
                .Where(column => IsIdLike(column.Name))
                .Take(8)
                .Select(column => column.Name)
                .ToList(),
            OutlierSummary = BuildOutlierSummary(profile, index),
            InconsistentFormats = BuildInconsistentFormats(profile, schema, quality),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList()
        };
    }

    private static List<InsightOutlierSummary> BuildOutlierSummary(DatasetProfile profile, DatasetIndex? index)
    {
        var output = new List<InsightOutlierSummary>();

        if (index?.Columns?.Count > 0)
        {
            foreach (var column in index.Columns)
            {
                var stats = column.NumericStats;
                if (stats == null || !stats.P5.HasValue || !stats.P95.HasValue || !stats.Min.HasValue || !stats.Max.HasValue)
                {
                    continue;
                }

                var spread = Math.Max(1e-9, stats.Max.Value - stats.Min.Value);
                var outlierRate = Math.Clamp((Math.Abs(stats.Min.Value - stats.P5.Value) + Math.Abs(stats.Max.Value - stats.P95.Value)) / spread, 0d, 1d);
                output.Add(new InsightOutlierSummary
                {
                    Column = column.Name,
                    OutlierRate = Math.Round(outlierRate, 6),
                    P5 = Math.Round(stats.P5.Value, 6),
                    P95 = Math.Round(stats.P95.Value, 6),
                    Iqr = 0
                });
            }

            return output.Take(10).ToList();
        }

        foreach (var column in profile.Columns.Where(c => c.InferredType.IsNumericLike() && c.Min.HasValue && c.Max.HasValue))
        {
            output.Add(new InsightOutlierSummary
            {
                Column = column.Name,
                OutlierRate = 0,
                P5 = Math.Round(column.Min!.Value, 6),
                P95 = Math.Round(column.Max!.Value, 6),
                Iqr = 0
            });
        }

        return output.Take(10).ToList();
    }

    private static List<InsightInconsistentFormat> BuildInconsistentFormats(
        DatasetProfile profile,
        DatasetImportSchema? schema,
        DatasetQualityEvidence quality)
    {
        if (quality.InvalidDateCount <= 0)
        {
            return [];
        }

        var dateColumns = schema?.Columns.Where(c => c.ConfirmedType == InferredType.Date).Select(c => c.Name).ToList()
            ?? profile.Columns.Where(c => c.InferredType == InferredType.Date).Select(c => c.Name).ToList();
        if (dateColumns.Count == 0)
        {
            return [];
        }

        var perColumn = Math.Max(1, quality.InvalidDateCount / dateColumns.Count);
        return dateColumns.Select(name => new InsightInconsistentFormat
        {
            Column = name,
            ParseFailuresCount = perColumn
        }).ToList();
    }

    private static InsightRelationshipsContext BuildRelationshipsV2(DatasetIndex? index, string? targetColumn)
    {
        var edges = index?.Correlations?.Edges ?? [];
        var topPositive = edges.Where(e => e.Score > 0).OrderByDescending(e => e.Score).Take(8).ToList();
        var topNegative = edges.Where(e => e.Score < 0).OrderBy(e => e.Score).Take(8).ToList();
        var targetPairs = string.IsNullOrWhiteSpace(targetColumn)
            ? new List<CorrelationEdge>()
            : edges.Where(e =>
                    string.Equals(e.LeftColumn, targetColumn, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.RightColumn, targetColumn, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => Math.Abs(e.Score))
                .Take(8)
                .ToList();

        return new InsightRelationshipsContext
        {
            CorrelationMatrixSummary = new InsightCorrelationMatrixSummary
            {
                TopPositivePairs = topPositive.Select(ToCorrelationPair).ToList(),
                TopNegativePairs = topNegative.Select(ToCorrelationPair).ToList(),
                TopPairsInvolvingTarget = targetPairs.Select(ToCorrelationPair).ToList()
            },
            FunctionalDependenciesHints = BuildFunctionalDependencies(index),
            TimeSeasonalityHints = new InsightTimeSeasonalityHints
            {
                WeekdayVsWeekendImpact = "Pending computation in v2 metrics stage",
                MonthOfYearEffect = "Pending computation in v2 metrics stage"
            }
        };
    }

    private static List<InsightFunctionalDependencyHint> BuildFunctionalDependencies(DatasetIndex? index)
    {
        if (index?.Columns?.Count == 0)
        {
            return [];
        }

        var idLike = index.Columns.Where(c => IsIdLike(c.Name)).ToList();
        var categories = index.Columns.Where(c => c.InferredType is InferredType.Category or InferredType.String).Take(8).ToList();
        var hints = new List<InsightFunctionalDependencyHint>();
        foreach (var cat in categories)
        {
            var dependent = idLike.FirstOrDefault(id => id.DistinctCount >= cat.DistinctCount && cat.DistinctCount > 0);
            if (dependent == null)
            {
                continue;
            }

            var confidence = Math.Clamp(cat.DistinctCount / (double)Math.Max(1, dependent.DistinctCount), 0d, 1d);
            hints.Add(new InsightFunctionalDependencyHint
            {
                Determinant = cat.Name,
                Dependent = dependent.Name,
                Confidence = Math.Round(confidence, 6)
            });
        }

        return hints.Take(6).ToList();
    }

    private static InsightTargetStory BuildTargetStoryV2(
        string targetColumn,
        List<InsightSchemaColumn> schemaColumns,
        IReadOnlyList<SeriesPoint> points,
        TimeSeriesStatsEvidence? timeSeriesStats,
        List<SegmentBreakdownEvidence> segmentBreakdowns,
        ChartRecommendation recommendation,
        ChartExecutionResponse chartResponse)
    {
        var targetSchema = schemaColumns.FirstOrDefault(c => string.Equals(c.Name, targetColumn, StringComparison.OrdinalIgnoreCase));
        var targetType = targetSchema?.SemanticType switch
        {
            "Money" => "Money",
            "Percentage" => "Percentage",
            _ => "Other"
        };

        var ordered = points
            .Where(point => point.Date != null)
            .OrderBy(point => point.Date)
            .ToList();

        var series = ordered
            .Select(point => new InsightTimeValuePoint
            {
                Time = point.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Value = Math.Round(point.Y, 6)
            })
            .Take(120)
            .ToList();

        var changePoints = timeSeriesStats?.ChangePoints.Select(item => new InsightChangePoint
        {
            Time = item.Position,
            Delta = Math.Round(item.ShiftMagnitude, 6)
        }).ToList() ?? new List<InsightChangePoint>();

        var offenders = segmentBreakdowns
            .OrderBy(item => item.ContributionValue)
            .Take(3)
            .Select(item => new InsightSegmentDelta
            {
                Dimension = "series",
                Category = item.Segment,
                Delta = Math.Round(item.ContributionValue, 6),
                Magnitude = Math.Round(Math.Abs(item.ContributionValue), 6)
            })
            .ToList();

        var positive = segmentBreakdowns
            .OrderByDescending(item => item.ContributionValue)
            .Take(3)
            .Select(item => new InsightSegmentDelta
            {
                Dimension = "series",
                Category = item.Segment,
                Delta = Math.Round(item.ContributionValue, 6),
                Magnitude = Math.Round(Math.Abs(item.ContributionValue), 6)
            })
            .ToList();

        return new InsightTargetStory
        {
            TargetType = targetType,
            TargetTrend = new InsightTargetTrend
            {
                ByTimeBin = recommendation.Query.X.Bin?.ToString()?.ToLowerInvariant() ?? "month",
                Series = series,
                VolatilityScore = Math.Round(timeSeriesStats?.VolatilityRatio ?? 0d, 6),
                ChangePoints = changePoints
            },
            TopSegmentsByTarget = new InsightTopSegmentsByTarget
            {
                DimensionCandidates = ["series"],
                Segments = segmentBreakdowns.Take(6).Select(item => new InsightSegmentImpact
                {
                    Dimension = "series",
                    Category = item.Segment,
                    AggregateValue = Math.Round(item.ContributionValue, 6),
                    Share = Math.Round(item.SharePercent, 6),
                    DeltaVsPreviousPeriod = 0
                }).ToList()
            },
            Offenders = new InsightOffenderSummary
            {
                TopNegativeSegments = offenders,
                TopPositiveSegments = positive
            },
            DriverCandidates = new InsightDriverCandidates
            {
                NumericDrivers = segmentBreakdowns.Take(6).Select(item => new InsightNumericDriver
                {
                    Feature = item.Segment,
                    CorrelationPearson = Math.Round(item.StabilityScore, 6),
                    CorrelationSpearman = Math.Round(item.StabilityScore * 0.95, 6),
                    MutualInfoApprox = Math.Round(item.SharePercent / 100d, 6),
                    StrengthLabel = item.SharePercent >= 20 ? "Strong" : (item.SharePercent >= 10 ? "Medium" : "Weak")
                }).ToList(),
                CategoricalDrivers =
                [
                    new InsightCategoricalDriver
                    {
                        Dimension = "series",
                        EffectSizeProxy = Math.Round(segmentBreakdowns.Select(item => item.SharePercent).DefaultIfEmpty(0).Max(), 6),
                        TopCategoriesImpact = segmentBreakdowns.Take(4).Select(item => new InsightSegmentImpact
                        {
                            Dimension = "series",
                            Category = item.Segment,
                            AggregateValue = Math.Round(item.ContributionValue, 6),
                            Share = Math.Round(item.SharePercent, 6),
                            DeltaVsPreviousPeriod = 0
                        }).ToList()
                    }
                ]
            },
            TargetDecompositionHints = new InsightTargetDecompositionHints
            {
                MoneyPackMeasures = recommendation.Query.YMetrics.Select(metric => metric.Column).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ScaleRatios = chartResponse.SeriesAxisAssignments
                    .Select(item => new InsightScaleRatioHint
                    {
                        Series = item.SeriesName,
                        ScaleRatioToPrimary = Math.Round(item.ScaleRatioToPrimary, 6),
                        RecommendedAxisIndex = item.RecommendedAxisIndex
                    })
                    .ToList()
            }
        };
    }

    private static List<InsightRecommendedAction> BuildActionsV2(
        IReadOnlyList<SegmentBreakdownEvidence> segmentBreakdowns,
        TimeSeriesStatsEvidence? timeSeriesStats)
    {
        var actions = new List<InsightRecommendedAction>();

        var biggest = segmentBreakdowns.OrderByDescending(item => Math.Abs(item.ContributionValue)).FirstOrDefault();
        if (biggest != null)
        {
            actions.Add(new InsightRecommendedAction
            {
                Title = $"Prioritize segment '{biggest.Segment}'",
                Rationale = "Largest contribution share indicates highest leverage for near-term impact.",
                EvidenceRefs = ["targetStory.topSegmentsByTarget", "evidenceIndex:D1"],
                ExpectedImpactDirection = "IncreaseTarget"
            });
        }

        if (timeSeriesStats != null && Math.Abs(timeSeriesStats.VolatilityRatio) > 0.3)
        {
            actions.Add(new InsightRecommendedAction
            {
                Title = "Reduce volatility in target trajectory",
                Rationale = "High volatility ratio increases planning risk and forecast uncertainty.",
                EvidenceRefs = ["targetStory.targetTrend", "evidenceIndex:T1"],
                ExpectedImpactDirection = "IncreaseTarget"
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(new InsightRecommendedAction
            {
                Title = "Validate target and segment definitions",
                Rationale = "No dominant pattern detected yet; ensure business segments and target objective are aligned.",
                EvidenceRefs = ["schemaContext.targetColumn", "dataQuality.warnings"],
                ExpectedImpactDirection = "IncreaseTarget"
            });
        }

        return actions.Take(5).ToList();
    }

    private static List<InsightEvidenceAnchor> BuildEvidenceAnchorsV2()
    {
        return
        [
            new InsightEvidenceAnchor { Id = "T1", Label = "Target trend summary", Path = "targetStory.targetTrend" },
            new InsightEvidenceAnchor { Id = "D1", Label = "Top driver candidate", Path = "targetStory.driverCandidates.numericDrivers[0]" },
            new InsightEvidenceAnchor { Id = "O1", Label = "Top negative offender", Path = "targetStory.offenders.topNegativeSegments[0]" },
            new InsightEvidenceAnchor { Id = "R1", Label = "Top relationship involving target", Path = "relationships.correlationMatrixSummary.topPairsInvolvingTarget[0]" },
            new InsightEvidenceAnchor { Id = "Q1", Label = "Highest missingness hotspot", Path = "dataQuality.nullHotspots[0]" }
        ];
    }

    private static InsightTimeRange? ResolveTimeRange(DatasetIndex? index)
    {
        var dates = index?.Columns
            .Select(column => column.DateStats)
            .Where(stats => stats?.Min != null && stats.Max != null)
            .ToList();
        if (dates == null || dates.Count == 0)
        {
            return null;
        }

        var min = dates.Min(item => item!.Min!.Value);
        var max = dates.Max(item => item!.Max!.Value);
        return new InsightTimeRange
        {
            Min = min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Max = max.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    private static InsightCorrelationPair ToCorrelationPair(CorrelationEdge edge)
    {
        return new InsightCorrelationPair
        {
            Left = edge.LeftColumn,
            Right = edge.RightColumn,
            Score = Math.Round(edge.Score, 6)
        };
    }

    private static string ResolveSemanticTypeLabel(InferredType type, string columnName)
    {
        var normalized = type.NormalizeLegacy();
        if (normalized == InferredType.Money)
        {
            return "Money";
        }

        if (normalized == InferredType.Percentage)
        {
            return "Percentage";
        }

        if (columnName.Contains("count", StringComparison.OrdinalIgnoreCase) ||
            columnName.Contains("qty", StringComparison.OrdinalIgnoreCase))
        {
            return "Count";
        }

        if (columnName.Contains("quantity", StringComparison.OrdinalIgnoreCase) ||
            columnName.Contains("quant", StringComparison.OrdinalIgnoreCase))
        {
            return "Quantity";
        }

        return "Generic";
    }

    private static string ResolveRoleHint(InferredType type, string columnName)
    {
        if (IsIdLike(columnName))
        {
            return "IdLike";
        }

        var normalized = type.NormalizeLegacy();
        if (normalized == InferredType.Date)
        {
            return "Time";
        }

        if (normalized.IsNumericLike())
        {
            return "Measure";
        }

        if (columnName.Contains("noise", StringComparison.OrdinalIgnoreCase))
        {
            return "Noise";
        }

        return "Dimension";
    }

    private static bool IsIdLike(string columnName)
    {
        var name = columnName.ToLowerInvariant();
        return name.EndsWith("id", StringComparison.Ordinal) ||
               name.Contains("_id", StringComparison.Ordinal) ||
               name.Contains("uuid", StringComparison.Ordinal) ||
               name.Contains("guid", StringComparison.Ordinal) ||
               name.Contains("token", StringComparison.Ordinal) ||
               name.Contains("hash", StringComparison.Ordinal);
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

    private static EvidencePack Clone(EvidencePack source)
    {
        var json = JsonSerializer.Serialize(source, SerializerOptions);
        return JsonSerializer.Deserialize<EvidencePack>(json, SerializerOptions) ?? new EvidencePack();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record CachedEvidencePack(DateTime StoredAtUtc, EvidencePack Pack);

    private sealed record SeriesPoint(
        string Series,
        string X,
        double Y,
        long Index,
        DateTime? Date,
        bool LooksLikeDate);
}
