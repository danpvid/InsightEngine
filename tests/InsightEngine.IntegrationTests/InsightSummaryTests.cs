using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Services;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class InsightSummaryTests
{
    [Fact]
    public void Build_WithIncreasingTimeSeries_ReturnsUpTrend()
    {
        var recommendation = BuildLineRecommendation();
        var executionResult = BuildLineExecutionResult(new List<double>
        {
            10, 12, 14, 16, 18, 20, 22, 24
        });

        var summary = InsightSummaryBuilder.Build(recommendation, executionResult);

        summary.Signals.Trend.Should().Be(TrendSignal.Up);
        summary.Signals.Volatility.Should().BeOneOf(VolatilitySignal.Low, VolatilitySignal.Medium);
        summary.Confidence.Should().BeGreaterThan(0.3);
    }

    [Fact]
    public void Build_WithOutlierValues_FlagsOutliers()
    {
        var recommendation = BuildBarRecommendation();
        var executionResult = BuildBarExecutionResult(new List<double>
        {
            10, 11, 9, 12, 10, 11, 950, 10, 9
        });

        var summary = InsightSummaryBuilder.Build(recommendation, executionResult);

        summary.Signals.Outliers.Should().NotBe(OutlierSignal.None);
        summary.BulletPoints.Should().Contain(b => b.Contains("outliers", StringComparison.OrdinalIgnoreCase));
    }

    private static ChartRecommendation BuildLineRecommendation()
    {
        return new ChartRecommendation
        {
            Id = "rec_line_test",
            Title = "Line Test",
            Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Line },
            Query = new ChartQuery
            {
                X = new FieldSpec
                {
                    Column = "date",
                    Role = AxisRole.Time,
                    Bin = TimeBin.Day
                },
                Y = new FieldSpec
                {
                    Column = "value",
                    Role = AxisRole.Measure,
                    Aggregation = Aggregation.Sum
                }
            }
        };
    }

    private static ChartRecommendation BuildBarRecommendation()
    {
        return new ChartRecommendation
        {
            Id = "rec_bar_test",
            Title = "Bar Test",
            Chart = new ChartMeta { Library = ChartLibrary.ECharts, Type = ChartType.Bar },
            Query = new ChartQuery
            {
                X = new FieldSpec
                {
                    Column = "category",
                    Role = AxisRole.Category
                },
                Y = new FieldSpec
                {
                    Column = "value",
                    Role = AxisRole.Measure,
                    Aggregation = Aggregation.Sum
                }
            }
        };
    }

    private static ChartExecutionResult BuildLineExecutionResult(List<double> values)
    {
        var baseTimestamp = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var data = values.Select((value, index) => new object?[]
        {
            baseTimestamp + (index * 86_400_000L),
            value
        }).ToList();

        var option = new EChartsOption
        {
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["type"] = "line",
                    ["data"] = data
                }
            }
        };

        return new ChartExecutionResult
        {
            Option = option
        };
    }

    private static ChartExecutionResult BuildBarExecutionResult(List<double> values)
    {
        var option = new EChartsOption
        {
            Series = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["type"] = "bar",
                    ["data"] = values
                }
            }
        };

        return new ChartExecutionResult
        {
            Option = option
        };
    }
}
