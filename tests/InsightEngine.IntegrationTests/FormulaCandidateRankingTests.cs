using FluentAssertions;
using InsightEngine.Domain.Models.FormulaDiscovery;
using InsightEngine.Infra.Data.Services.FormulaDiscovery;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class FormulaCandidateRankingTests
{
    [Fact]
    public void BuildCandidates_WhenDeterministicLike_ShouldEarlyStopAtLinearModel()
    {
        var ranking = new FormulaCandidateRankingService(
            new LinearRegressionService(),
            new FormulaExpressionFormatter());

        var sample = new FormulaSampleSet
        {
            TargetColumn = "total",
            FeatureColumns = new List<string> { "unit_price", "quantity" },
            X =
            [
                [10d, 1d],
                [12d, 2d],
                [14d, 3d],
                [16d, 4d],
                [18d, 5d],
                [20d, 6d]
            ],
            Y = [16d, 22d, 28d, 34d, 40d, 46d],
            OriginalRowCount = 6,
            AcceptedRowCount = 6,
            DroppedRowCount = 0
        };

        var candidates = ranking.BuildCandidates(
            sample,
            maxCandidates: 5,
            enableInteractions: true,
            enableRatios: true,
            minR2Improvement: 0d);

        candidates.Should().HaveCount(1);
        candidates[0].ModelType.Should().Be(FormulaModelType.Linear);
        candidates[0].Confidence.Should().Be(FormulaConfidenceLevel.DeterministicLike);
    }

    [Fact]
    public void BuildCandidates_ShouldRespectTopKMaxCandidates()
    {
        var ranking = new FormulaCandidateRankingService(
            new LinearRegressionService(),
            new FormulaExpressionFormatter());

        var sample = new FormulaSampleSet
        {
            TargetColumn = "y",
            FeatureColumns = new List<string> { "x1", "x2", "x3" },
            X =
            [
                [1d, 2d, 0.5d],
                [2d, 1d, 0.2d],
                [3d, 4d, 0.8d],
                [4d, 3d, 0.3d],
                [5d, 7d, 1.0d],
                [6d, 5d, 0.7d],
                [7d, 9d, 1.2d],
                [8d, 8d, 1.1d]
            ],
            Y = [5.1d, 5.4d, 10.8d, 11.2d, 17.4d, 16.6d, 23.9d, 24.1d],
            OriginalRowCount = 8,
            AcceptedRowCount = 8,
            DroppedRowCount = 0
        };

        var candidates = ranking.BuildCandidates(
            sample,
            maxCandidates: 2,
            enableInteractions: true,
            enableRatios: true,
            minR2Improvement: 0d);

        candidates.Count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public void FormulaFormatter_ShouldOrderTermsByAbsoluteCoefficient()
    {
        var formatter = new FormulaExpressionFormatter();
        var formula = formatter.BuildPrettyFormula(
            "total",
            intercept: 1,
            terms:
            [
                new Term { FeatureName = "small", Coefficient = 0.2d },
                new Term { FeatureName = "large", Coefficient = 5d }
            ]);

        formula.Should().Contain("total ≈");
        formula.IndexOf("*large", StringComparison.Ordinal).Should().BeLessThan(formula.IndexOf("*small", StringComparison.Ordinal));
    }
}
