using FluentAssertions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Charts;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Recommendations.Scoring;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class RecommendationScoringTests
{
    [Fact]
    public void HigherCorrelation_ShouldScoreHigher_WhenOtherFactorsEqual()
    {
        var scorer = new ChartRelevanceScorer();
        var weights = new RecommendationWeights();

        var high = BuildIndex(correlation: 0.9, nullRate: 0.1, distinct: 20, stdDev: 10);
        var low = BuildIndex(correlation: 0.1, nullRate: 0.1, distinct: 20, stdDev: 10);
        var recommendation = BuildRecommendation("feature");

        var highScore = scorer.Score(recommendation, high, weights).Score;
        var lowScore = scorer.Score(recommendation, low, weights).Score;

        highScore.Should().BeGreaterThan(lowScore);
    }

    [Fact]
    public void HighNullRate_ShouldReduceScore()
    {
        var scorer = new ChartRelevanceScorer();
        var weights = new RecommendationWeights();

        var lowNull = BuildIndex(correlation: 0.5, nullRate: 0.05, distinct: 20, stdDev: 10);
        var highNull = BuildIndex(correlation: 0.5, nullRate: 0.7, distinct: 20, stdDev: 10);
        var recommendation = BuildRecommendation("feature");

        var lowNullScore = scorer.Score(recommendation, lowNull, weights).Score;
        var highNullScore = scorer.Score(recommendation, highNull, weights).Score;

        lowNullScore.Should().BeGreaterThan(highNullScore);
    }

    [Fact]
    public void HighCardinality_ShouldReduceScore_ForCategoricalLikeColumn()
    {
        var scorer = new ChartRelevanceScorer();
        var weights = new RecommendationWeights();

        var lowCard = BuildIndex(correlation: 0.5, nullRate: 0.1, distinct: 3, stdDev: 10);
        var highCard = BuildIndex(correlation: 0.5, nullRate: 0.1, distinct: 900, stdDev: 10, rows: 1000);
        var recommendation = BuildRecommendation("feature");

        var lowCardScore = scorer.Score(recommendation, lowCard, weights).Score;
        var highCardScore = scorer.Score(recommendation, highCard, weights).Score;

        lowCardScore.Should().BeGreaterThan(highCardScore);
    }

    [Fact]
    public void NearConstantColumns_ShouldBeHeavilyPenalized()
    {
        var scorer = new ChartRelevanceScorer();
        var weights = new RecommendationWeights();

        var normal = BuildIndex(correlation: 0.5, nullRate: 0.1, distinct: 20, stdDev: 10);
        var constant = BuildIndex(correlation: 0.5, nullRate: 0.1, distinct: 1, stdDev: 0);
        var recommendation = BuildRecommendation("feature");

        var normalScore = scorer.Score(recommendation, normal, weights).Score;
        var constantScore = scorer.Score(recommendation, constant, weights).Score;

        normalScore.Should().BeGreaterThan(constantScore);
    }

    [Fact]
    public void VarianceNormalization_ShouldBeStable_WhenValuesAreIdentical()
    {
        var values = new List<double> { 5, 5, 5, 5 };
        var normalized = Normalization.RobustVarianceNormalize(5, values);

        normalized.Should().Be(0);
    }

    private static ChartRecommendation BuildRecommendation(string feature)
    {
        return new ChartRecommendation
        {
            Id = "rec_001",
            Title = "Test",
            Chart = new ChartMeta { Type = ChartType.Bar, Library = ChartLibrary.ECharts },
            Query = new ChartQuery
            {
                X = new FieldSpec { Column = feature, Role = AxisRole.Category },
                Y = new FieldSpec { Column = "target", Role = AxisRole.Measure, Aggregation = Aggregation.Sum }
            }
        };
    }

    private static DatasetIndex BuildIndex(double correlation, double nullRate, long distinct, double stdDev, long rows = 1000)
    {
        return new DatasetIndex
        {
            DatasetId = Guid.NewGuid(),
            RowCount = rows,
            ColumnCount = 2,
            TargetColumn = "target",
            Columns =
            [
                new ColumnIndex
                {
                    Name = "target",
                    InferredType = InferredType.Number,
                    NullRate = 0,
                    DistinctCount = 100,
                    NumericStats = new NumericStatsIndex { StdDev = 15, P90 = 30, P50 = 10 }
                },
                new ColumnIndex
                {
                    Name = "feature",
                    InferredType = InferredType.Number,
                    NullRate = nullRate,
                    DistinctCount = distinct,
                    NumericStats = new NumericStatsIndex { StdDev = stdDev, P90 = stdDev * 2, P50 = stdDev }
                }
            ],
            Correlations = new CorrelationIndex
            {
                Edges =
                [
                    new CorrelationEdge
                    {
                        LeftColumn = "target",
                        RightColumn = "feature",
                        Score = correlation,
                        Method = CorrelationMethod.Pearson,
                        Strength = CorrelationStrength.Medium,
                        Direction = CorrelationDirection.Positive,
                        SampleSize = rows,
                        Confidence = ConfidenceLevel.Medium
                    }
                ]
            }
        };
    }
}
