using System.Text;
using System.Text.Json;
using FluentAssertions;
using InsightEngine.Application.Insights;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Charts;
using InsightEngine.Domain.Models.MetadataIndex;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class LlmInsightComposerV2Tests
{
    [Fact]
    public void Payload_ShouldIncludeTargetColumn()
    {
        var composer = new LlmInsightComposerV2();
        var index = BuildIndex(12);
        var recommendations = BuildRecommendations();

        var payload = composer.Compose(index, recommendations, "pt-br", ignoredColumns: []).PayloadJson;
        using var doc = JsonDocument.Parse(payload);

        doc.RootElement.GetProperty("targetColumn").GetString().Should().Be("target");
    }

    [Fact]
    public void Payload_ShouldLimitTopNCollections()
    {
        var composer = new LlmInsightComposerV2();
        var index = BuildIndex(40);
        var recommendations = BuildRecommendations(20);

        var payload = composer.Compose(index, recommendations, "pt-br", ignoredColumns: []).PayloadJson;
        using var doc = JsonDocument.Parse(payload);

        doc.RootElement.GetProperty("topCorrelatedFeatures").GetArrayLength().Should().BeLessOrEqualTo(8);
        doc.RootElement.GetProperty("highVarianceFeatures").GetArrayLength().Should().BeLessOrEqualTo(8);
        doc.RootElement.GetProperty("highNullRateFeatures").GetArrayLength().Should().BeLessOrEqualTo(5);
        doc.RootElement.GetProperty("outlierColumns").GetArrayLength().Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public void Payload_ShouldNotExceedSizeLimit()
    {
        var composer = new LlmInsightComposerV2();
        var index = BuildIndex(80);
        var recommendations = BuildRecommendations(40);

        var payload = composer.Compose(index, recommendations, "pt-br", ignoredColumns: []).PayloadJson;
        Encoding.UTF8.GetByteCount(payload).Should().BeLessOrEqualTo(25 * 1024);
    }

    [Fact]
    public void Payload_ShouldNotIncludeIgnoredColumns()
    {
        var composer = new LlmInsightComposerV2();
        var index = BuildIndex(12);
        var recommendations = BuildRecommendations();

        var payload = composer.Compose(index, recommendations, "pt-br", ignoredColumns: ["feature_3"]).PayloadJson;
        payload.ToLowerInvariant().Should().NotContain("feature_3");
    }

    private static DatasetIndex BuildIndex(int featureCount)
    {
        var columns = new List<ColumnIndex>
        {
            new()
            {
                Name = "target",
                InferredType = InferredType.Number,
                NullRate = 0.02,
                DistinctCount = 300,
                NumericStats = new NumericStatsIndex { P50 = 50, P90 = 100, StdDev = 20 }
            }
        };

        var edges = new List<CorrelationEdge>();
        for (var i = 1; i <= featureCount; i++)
        {
            columns.Add(new ColumnIndex
            {
                Name = $"feature_{i}",
                InferredType = i % 5 == 0 ? InferredType.Category : InferredType.Number,
                NullRate = Math.Min(0.9, i * 0.01),
                DistinctCount = 10 + i,
                TopValues = ["A", "B", "C"],
                NumericStats = new NumericStatsIndex
                {
                    P50 = 10 + i,
                    P90 = 20 + (i * 2),
                    StdDev = 1 + i
                }
            });

            edges.Add(new CorrelationEdge
            {
                LeftColumn = "target",
                RightColumn = $"feature_{i}",
                Score = Math.Max(-1, 1 - (i * 0.02)),
                Method = CorrelationMethod.Pearson,
                Strength = CorrelationStrength.Medium,
                Direction = CorrelationDirection.Positive,
                SampleSize = 1000,
                Confidence = ConfidenceLevel.Medium
            });
        }

        return new DatasetIndex
        {
            DatasetId = Guid.NewGuid(),
            RowCount = 1000,
            ColumnCount = columns.Count,
            TargetColumn = "target",
            Columns = columns,
            Correlations = new CorrelationIndex { Edges = edges }
        };
    }

    private static List<ChartRecommendation> BuildRecommendations(int count = 12)
    {
        var output = new List<ChartRecommendation>();
        for (var i = 1; i <= count; i++)
        {
            output.Add(new ChartRecommendation
            {
                Id = $"rec_{i:000}",
                Title = $"Chart {i}",
                Reason = "Test reason",
                Chart = new ChartMeta { Type = ChartType.Bar, Library = ChartLibrary.ECharts },
                Query = new ChartQuery
                {
                    X = new FieldSpec { Column = $"feature_{i}", Role = AxisRole.Category },
                    Y = new FieldSpec { Column = "target", Role = AxisRole.Measure, Aggregation = Aggregation.Sum }
                }
            });
        }

        return output;
    }
}
