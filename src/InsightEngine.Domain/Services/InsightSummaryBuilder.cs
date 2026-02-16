using System.Globalization;
using System.Linq;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Services;

public static class InsightSummaryBuilder
{
    private const int MaxOutliersInSummary = 5;
    private const double TrendThreshold = 0.05;
    private const double VolatilityLow = 0.2;
    private const double VolatilityHigh = 0.5;

    public static InsightSummary Build(ChartRecommendation recommendation, ChartExecutionResult executionResult)
    {
        var values = ExtractSeriesValues(executionResult);
        if (values.Count == 0)
        {
            return BuildFallback(recommendation);
        }

        var stats = ComputeStats(values);
        var isTimeSeries = recommendation.Chart.Type == ChartType.Line;

        var trendResult = isTimeSeries ? ComputeTrend(values, stats.Mean) : (TrendSignal.Flat, 0.0);
        var volatilitySignal = ClassifyVolatility(stats.CoefficientOfVariation);
        var outlierResult = ComputeOutliers(values, stats.Mean, stats.StandardDeviation);
        var seasonalityResult = isTimeSeries ? ComputeSeasonality(values, recommendation.Query.X.Bin) : (SeasonalitySignal.None, 0.0);

        var headline = BuildHeadline(recommendation, trendResult.Item1, volatilitySignal, isTimeSeries);
        var bullets = BuildBullets(stats, trendResult, volatilitySignal, outlierResult, seasonalityResult, isTimeSeries);

        var confidence = ComputeConfidence(
            values.Count,
            trendResult.Item1,
            outlierResult.Signal,
            seasonalityResult.Item1);

        return new InsightSummary
        {
            Headline = headline,
            BulletPoints = bullets,
            Signals = new InsightSignals
            {
                Trend = trendResult.Item1,
                Volatility = volatilitySignal,
                Outliers = outlierResult.Signal,
                Seasonality = seasonalityResult.Item1
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

    private static List<double> ExtractSeriesValues(ChartExecutionResult executionResult)
    {
        var values = new List<double>();
        var series = executionResult.Option.Series?.FirstOrDefault();
        if (series == null || !series.TryGetValue("data", out var dataObject) || dataObject == null)
        {
            return values;
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
                        values.Add(y);
                    }
                    continue;
                }

                if (item is System.Collections.IList list && list.Count >= 2)
                {
                    if (TryToDouble(list[1], out var y))
                    {
                        values.Add(y);
                    }
                    continue;
                }

                if (TryToDouble(item, out var value))
                {
                    values.Add(value);
                }
            }
        }

        values.RemoveAll(v => double.IsNaN(v) || double.IsInfinity(v));
        return values;
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

    private static (TrendSignal Signal, double DeltaPct) ComputeTrend(List<double> values, double mean)
    {
        if (values.Count < 4)
        {
            return (TrendSignal.Flat, 0.0);
        }

        var window = Math.Max(1, values.Count / 4);
        var firstMean = values.Take(window).Average();
        var lastMean = values.TakeLast(window).Average();
        var delta = lastMean - firstMean;
        var deltaPct = delta / Math.Max(Math.Abs(mean), 1e-9);

        if (Math.Abs(deltaPct) < TrendThreshold)
        {
            return (TrendSignal.Flat, deltaPct);
        }

        return deltaPct > 0 ? (TrendSignal.Up, deltaPct) : (TrendSignal.Down, deltaPct);
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

    private static (OutlierSignal Signal, int Count, List<double> TopValues) ComputeOutliers(
        List<double> values,
        double mean,
        double std)
    {
        if (values.Count < 4)
        {
            return (OutlierSignal.None, 0, new List<double>());
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
            return (OutlierSignal.None, 0, new List<double>());
        }

        var ratio = (double)count / values.Count;
        var signal = ratio <= 0.02 || count <= 3 ? OutlierSignal.Few : OutlierSignal.Many;

        return (signal, count, topValues);
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

    private static (SeasonalitySignal Signal, double Correlation) ComputeSeasonality(List<double> values, TimeBin? bin)
    {
        var lag = bin switch
        {
            TimeBin.Day => 7,
            TimeBin.Week => 4,
            TimeBin.Month => 12,
            TimeBin.Quarter => 4,
            TimeBin.Year => 2,
            _ => 0
        };

        if (lag <= 0 || values.Count < lag * 2)
        {
            return (SeasonalitySignal.None, 0.0);
        }

        var correlation = ComputeLagCorrelation(values, lag);
        if (double.IsNaN(correlation))
        {
            return (SeasonalitySignal.None, 0.0);
        }

        if (correlation >= 0.6)
        {
            return (SeasonalitySignal.Strong, correlation);
        }

        if (correlation >= 0.35)
        {
            return (SeasonalitySignal.Weak, correlation);
        }

        return (SeasonalitySignal.None, correlation);
    }

    private static double ComputeLagCorrelation(List<double> values, int lag)
    {
        var n = values.Count - lag;
        if (n <= 1)
        {
            return 0.0;
        }

        var sumA = 0.0;
        var sumB = 0.0;
        var sumA2 = 0.0;
        var sumB2 = 0.0;
        var sumAB = 0.0;

        for (var i = 0; i < n; i++)
        {
            var a = values[i];
            var b = values[i + lag];

            sumA += a;
            sumB += b;
            sumA2 += a * a;
            sumB2 += b * b;
            sumAB += a * b;
        }

        var numerator = (n * sumAB) - (sumA * sumB);
        var denomLeft = (n * sumA2) - (sumA * sumA);
        var denomRight = (n * sumB2) - (sumB * sumB);
        var denominator = Math.Sqrt(denomLeft * denomRight);

        if (denominator <= 1e-9)
        {
            return 0.0;
        }

        return numerator / denominator;
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
        (TrendSignal Signal, double DeltaPct) trendResult,
        VolatilitySignal volatility,
        (OutlierSignal Signal, int Count, List<double> TopValues) outlierResult,
        (SeasonalitySignal Signal, double Correlation) seasonalityResult,
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
            bullets.Add($"Sazonalidade {LabelSeasonality(seasonalityResult.Signal)} (corr {FormatNumber(seasonalityResult.Correlation, 2)}).");
        }

        return bullets;
    }

    private static string BuildTrendBullet((TrendSignal Signal, double DeltaPct) trendResult)
    {
        var direction = trendResult.Signal switch
        {
            TrendSignal.Up => "Alta",
            TrendSignal.Down => "Queda",
            _ => "Estavel"
        };

        var pct = Math.Abs(trendResult.DeltaPct);
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
        int count,
        TrendSignal trend,
        OutlierSignal outliers,
        SeasonalitySignal seasonality)
    {
        var confidence = 0.3;

        if (count >= 10)
        {
            confidence += 0.2;
        }

        if (count >= 30)
        {
            confidence += 0.15;
        }

        if (count >= 100)
        {
            confidence += 0.1;
        }

        if (trend != TrendSignal.Flat)
        {
            confidence += 0.05;
        }

        if (outliers != OutlierSignal.None)
        {
            confidence += 0.05;
        }

        if (seasonality != SeasonalitySignal.None)
        {
            confidence += 0.05;
        }

        return Math.Min(0.9, Math.Max(0.1, confidence));
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
}
