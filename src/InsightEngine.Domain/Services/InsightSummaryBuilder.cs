using System.Globalization;
using System.Linq;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Services;

public static class InsightSummaryBuilder
{
    private const int MaxOutliersInSummary = 5;
    private const double TrendFlatThreshold = 0.06;
    private const double VolatilityLow = 0.15;
    private const double VolatilityHigh = 0.45;

    public static InsightSummary Build(ChartRecommendation recommendation, ChartExecutionResult executionResult)
    {
        var points = ExtractSeriesPoints(executionResult);
        var values = points.Select(p => p.Value).ToList();
        if (values.Count == 0)
        {
            return BuildFallback(recommendation);
        }

        var stats = ComputeStats(values);
        var isTimeSeries = recommendation.Chart.Type == ChartType.Line;
        var effectiveRowCount = executionResult.RowCount > 0 ? executionResult.RowCount : values.Count;

        var trendResult = isTimeSeries ? ComputeTrend(values, stats.Mean) : new TrendComputationResult(TrendSignal.Flat, 0.0, 0.0);
        var volatilitySignal = ClassifyVolatility(stats.CoefficientOfVariation);
        var outlierResult = ComputeOutliers(values, stats.Mean, stats.StandardDeviation);
        var seasonalityResult = isTimeSeries
            ? ComputeSeasonality(points, recommendation.Query.X.Bin, stats.StandardDeviation)
            : new SeasonalityComputationResult(SeasonalitySignal.None, 0.0, 0.0);

        var headline = BuildHeadline(recommendation, trendResult.Signal, volatilitySignal, isTimeSeries);
        var bullets = BuildBullets(stats, trendResult, volatilitySignal, outlierResult, seasonalityResult, isTimeSeries);

        var confidence = ComputeConfidence(
            effectiveRowCount,
            trendResult.Strength,
            volatilitySignal,
            outlierResult.Strength,
            seasonalityResult.Strength);

        return new InsightSummary
        {
            Headline = headline,
            BulletPoints = bullets,
            Signals = new InsightSignals
            {
                Trend = trendResult.Signal,
                Volatility = volatilitySignal,
                Outliers = outlierResult.Signal,
                Seasonality = seasonalityResult.Signal
            },
            Confidence = confidence
        };
    }

    private static InsightSummary BuildFallback(ChartRecommendation recommendation)
    {
        return new InsightSummary
        {
            Headline = $"Insight indisponivel para {recommendation.Title}",
            BulletPoints = new List<string>
            {
                "Nao ha dados suficientes para gerar um resumo confiavel."
            },
            Signals = new InsightSignals
            {
                Trend = TrendSignal.Flat,
                Volatility = VolatilitySignal.Medium,
                Outliers = OutlierSignal.None,
                Seasonality = SeasonalitySignal.None
            },
            Confidence = 0.2
        };
    }

    private static List<SeriesPoint> ExtractSeriesPoints(ChartExecutionResult executionResult)
    {
        var points = new List<SeriesPoint>();
        var series = executionResult.Option.Series?.FirstOrDefault();
        if (series == null || !series.TryGetValue("data", out var dataObject) || dataObject == null)
        {
            return points;
        }

        if (dataObject is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                if (item is object[] pair && pair.Length >= 2)
                {
                    if (TryToDouble(pair[1], out var y))
                    {
                        points.Add(new SeriesPoint(y, TryToDateTimeOffset(pair[0])));
                    }
                    continue;
                }

                if (item is System.Collections.IList list && list.Count >= 2)
                {
                    if (TryToDouble(list[1], out var y))
                    {
                        points.Add(new SeriesPoint(y, TryToDateTimeOffset(list[0])));
                    }
                    continue;
                }

                if (TryToDouble(item, out var value))
                {
                    points.Add(new SeriesPoint(value, null));
                }
            }
        }

        points.RemoveAll(point => double.IsNaN(point.Value) || double.IsInfinity(point.Value));
        return points;
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
        }

        result = 0;
        return false;
    }

    private static DateTimeOffset? TryToDateTimeOffset(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto;
        }

        if (value is DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime());
        }

        if (TryToDouble(value, out var numericValue))
        {
            // Unix milliseconds is the expected axis format from ECharts time series.
            if (numericValue > 10_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(numericValue));
            }

            if (numericValue > 1_000_000_000)
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)Math.Round(numericValue));
            }
        }

        if (value is string text &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static (double Mean, double StandardDeviation, double CoefficientOfVariation, double Min, double Max) ComputeStats(List<double> values)
    {
        var count = values.Count;
        var sum = 0.0;
        var sumSquares = 0.0;
        var min = double.MaxValue;
        var max = double.MinValue;

        foreach (var v in values)
        {
            sum += v;
            sumSquares += v * v;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        var mean = sum / count;
        var variance = (sumSquares / count) - (mean * mean);
        if (variance < 0)
        {
            variance = 0;
        }

        var std = Math.Sqrt(variance);
        var cv = std / Math.Max(Math.Abs(mean), 1e-9);

        return (mean, std, cv, min, max);
    }

    private static TrendComputationResult ComputeTrend(List<double> values, double mean)
    {
        if (values.Count < 4)
        {
            return new TrendComputationResult(TrendSignal.Flat, 0.0, 0.0);
        }

        var n = values.Count;
        var xMean = (n - 1) / 2.0;
        var denominator = 0.0;
        var numerator = 0.0;

        for (var i = 0; i < n; i++)
        {
            var dx = i - xMean;
            denominator += dx * dx;
            numerator += dx * (values[i] - mean);
        }

        if (denominator <= 1e-9)
        {
            return new TrendComputationResult(TrendSignal.Flat, 0.0, 0.0);
        }

        var slope = numerator / denominator;
        var normalizedChange = (slope * (n - 1)) / Math.Max(Math.Abs(mean), 1e-9);

        if (Math.Abs(normalizedChange) < TrendFlatThreshold)
        {
            var flatStrength = Math.Min(1.0, Math.Abs(normalizedChange) / TrendFlatThreshold);
            return new TrendComputationResult(TrendSignal.Flat, normalizedChange, flatStrength);
        }

        var signal = normalizedChange > 0 ? TrendSignal.Up : TrendSignal.Down;
        var strength = Math.Min(1.0, Math.Abs(normalizedChange) / 0.35);
        return new TrendComputationResult(signal, normalizedChange, strength);
    }

    private static VolatilitySignal ClassifyVolatility(double cv)
    {
        if (cv < VolatilityLow)
        {
            return VolatilitySignal.Low;
        }

        if (cv < VolatilityHigh)
        {
            return VolatilitySignal.Medium;
        }

        return VolatilitySignal.High;
    }

    private static OutlierComputationResult ComputeOutliers(
        List<double> values,
        double mean,
        double std)
    {
        if (values.Count < 4)
        {
            return new OutlierComputationResult(OutlierSignal.None, 0, new List<double>(), 0.0);
        }

        var sorted = values.ToArray();
        Array.Sort(sorted);

        var q1 = Percentile(sorted, 0.25);
        var q3 = Percentile(sorted, 0.75);
        var iqr = q3 - q1;

        var outliers = new List<(double Value, double Distance)>();

        if (iqr > 1e-9)
        {
            var lower = q1 - (1.5 * iqr);
            var upper = q3 + (1.5 * iqr);

            foreach (var value in values)
            {
                if (value < lower)
                {
                    outliers.Add((value, lower - value));
                }
                else if (value > upper)
                {
                    outliers.Add((value, value - upper));
                }
            }
        }
        else if (std > 1e-9)
        {
            foreach (var value in values)
            {
                var z = Math.Abs((value - mean) / std);
                if (z >= 3.0)
                {
                    outliers.Add((value, z));
                }
            }
        }

        var count = outliers.Count;
        var topValues = outliers
            .OrderByDescending(o => o.Distance)
            .Take(MaxOutliersInSummary)
            .Select(o => o.Value)
            .ToList();

        if (count == 0)
        {
            return new OutlierComputationResult(OutlierSignal.None, 0, new List<double>(), 0.0);
        }

        var ratio = (double)count / values.Count;
        var signal = ratio <= 0.02 || count <= 3 ? OutlierSignal.Few : OutlierSignal.Many;
        var strength = Math.Min(1.0, ratio * 8);
        return new OutlierComputationResult(signal, count, topValues, strength);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var position = (sorted.Length - 1) * percentile;
        var left = (int)Math.Floor(position);
        var right = (int)Math.Ceiling(position);

        if (left == right)
        {
            return sorted[left];
        }

        var weight = position - left;
        return (sorted[left] * (1 - weight)) + (sorted[right] * weight);
    }

    private static SeasonalityComputationResult ComputeSeasonality(
        List<SeriesPoint> points,
        TimeBin? bin,
        double overallStd)
    {
        if (bin != TimeBin.Day || points.Count < 14)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.None, 0.0, 0.0);
        }

        var datedPoints = points
            .Where(point => point.Timestamp.HasValue)
            .Select(point => new { Timestamp = point.Timestamp!.Value, point.Value })
            .ToList();

        if (datedPoints.Count < 14)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.None, 0.0, 0.0);
        }

        var spanDays = (datedPoints.Max(point => point.Timestamp) - datedPoints.Min(point => point.Timestamp)).TotalDays;
        if (spanDays < 60)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.None, 0.0, 0.0);
        }

        var weekdayMeans = datedPoints
            .GroupBy(point => point.Timestamp.DayOfWeek)
            .Select(group => group.Average(item => item.Value))
            .ToList();

        if (weekdayMeans.Count < 4)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.None, 0.0, 0.0);
        }

        var weekdayMean = weekdayMeans.Average();
        var weekdayVariance = weekdayMeans
            .Select(mean => (mean - weekdayMean) * (mean - weekdayMean))
            .Average();
        var weekdayStd = Math.Sqrt(Math.Max(weekdayVariance, 0));
        var strength = weekdayStd / Math.Max(overallStd, 1e-9);

        if (strength >= 0.45)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.Strong, strength, Math.Min(1.0, strength));
        }

        if (strength >= 0.25)
        {
            return new SeasonalityComputationResult(SeasonalitySignal.Weak, strength, Math.Min(1.0, strength));
        }

        return new SeasonalityComputationResult(SeasonalitySignal.None, strength, Math.Min(1.0, strength * 0.5));
    }

    private static string BuildHeadline(
        ChartRecommendation recommendation,
        TrendSignal trend,
        VolatilitySignal volatility,
        bool isTimeSeries)
    {
        var volatilityLabel = LabelVolatility(volatility);

        if (isTimeSeries)
        {
            return trend switch
            {
                TrendSignal.Up => $"Tendencia de alta com volatilidade {volatilityLabel}",
                TrendSignal.Down => $"Tendencia de queda com volatilidade {volatilityLabel}",
                _ => $"Sem tendencia clara; volatilidade {volatilityLabel}"
            };
        }

        return recommendation.Chart.Type switch
        {
            ChartType.Bar => $"Variacao entre categorias com volatilidade {volatilityLabel}",
            ChartType.Scatter => $"Dispersao {volatilityLabel} entre metricas",
            ChartType.Histogram => $"Distribuicao {volatilityLabel} de valores",
            _ => $"Resumo com volatilidade {volatilityLabel}"
        };
    }

    private static List<string> BuildBullets(
        (double Mean, double StandardDeviation, double CoefficientOfVariation, double Min, double Max) stats,
        TrendComputationResult trendResult,
        VolatilitySignal volatility,
        OutlierComputationResult outlierResult,
        SeasonalityComputationResult seasonalityResult,
        bool isTimeSeries)
    {
        var bullets = new List<string>();

        if (!isTimeSeries)
        {
            bullets.Add($"Faixa de valores: {FormatNumber(stats.Min)} a {FormatNumber(stats.Max)}.");
        }
        else
        {
            bullets.Add(BuildTrendBullet(trendResult));
        }

        bullets.Add($"Volatilidade {LabelVolatility(volatility)} (CV {FormatNumber(stats.CoefficientOfVariation, 2)}).");

        if (outlierResult.Count > 0)
        {
            var topValues = string.Join(", ", outlierResult.TopValues.Select(v => FormatNumber(v)));
            bullets.Add($"{outlierResult.Count} outliers detectados. Maiores: {topValues}.");
        }
        else
        {
            bullets.Add("Sem outliers relevantes.");
        }

        if (isTimeSeries && seasonalityResult.Signal != SeasonalitySignal.None)
        {
            bullets.Add($"Sazonalidade {LabelSeasonality(seasonalityResult.Signal)} (forca {FormatNumber(seasonalityResult.Strength, 2)}).");
        }

        return bullets;
    }

    private static string BuildTrendBullet(TrendComputationResult trendResult)
    {
        var direction = trendResult.Signal switch
        {
            TrendSignal.Up => "Alta",
            TrendSignal.Down => "Queda",
            _ => "Estavel"
        };

        var pct = Math.Abs(trendResult.NormalizedChange);
        return $"{direction} aproximada de {FormatPercent(pct)} entre inicio e fim.";
    }

    private static string LabelVolatility(VolatilitySignal signal)
    {
        return signal switch
        {
            VolatilitySignal.Low => "baixa",
            VolatilitySignal.High => "alta",
            _ => "moderada"
        };
    }

    private static string LabelSeasonality(SeasonalitySignal signal)
    {
        return signal switch
        {
            SeasonalitySignal.Strong => "forte",
            SeasonalitySignal.Weak => "fraca",
            _ => "nenhuma"
        };
    }

    private static double ComputeConfidence(
        int rowCountReturned,
        double trendStrength,
        VolatilitySignal volatility,
        double outlierStrength,
        double seasonalityStrength)
    {
        var rowScore = rowCountReturned switch
        {
            >= 500 => 0.4,
            >= 100 => 0.3,
            >= 30 => 0.2,
            >= 10 => 0.12,
            _ => 0.05
        };

        var volatilityStrength = volatility switch
        {
            VolatilitySignal.High => 0.35,
            VolatilitySignal.Medium => 0.2,
            _ => 0.1
        };

        var signalScore = (trendStrength + outlierStrength + seasonalityStrength + volatilityStrength) / 4.0;
        var confidence = 0.2 + rowScore + (signalScore * 0.35);

        return Math.Min(0.95, Math.Max(0.1, confidence));
    }

    private static string FormatNumber(double value, int decimals = 1)
    {
        return value.ToString($"0.{new string('#', decimals)}", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(double value)
    {
        var pct = value * 100;
        return pct.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private readonly record struct SeriesPoint(double Value, DateTimeOffset? Timestamp);
    private readonly record struct TrendComputationResult(TrendSignal Signal, double NormalizedChange, double Strength);
    private readonly record struct OutlierComputationResult(OutlierSignal Signal, int Count, List<double> TopValues, double Strength);
    private readonly record struct SeasonalityComputationResult(SeasonalitySignal Signal, double Score, double Strength);
}
