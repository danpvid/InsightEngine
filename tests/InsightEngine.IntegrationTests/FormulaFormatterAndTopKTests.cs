using FluentAssertions;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models.FormulaDiscovery;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Services;
using InsightEngine.Infra.Data.Services.FormulaDiscovery;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class FormulaFormatterAndTopKTests
{
    [Fact]
    public void FormulaFormatter_ShouldDropZeroAndUnitCoefficients()
    {
        var formatter = new FormulaExpressionFormatter();
        var text = formatter.BuildPrettyFormula(
            "total",
            43,
            [
                new Term { FeatureName = "field1", Coefficient = 1d },
                new Term { FeatureName = "field2", Coefficient = -1d },
                new Term { FeatureName = "field3", Coefficient = 0d },
                new Term { FeatureName = "field5/((1*field6)+eps)", Coefficient = 2d }
            ]);

        text.Should().Contain("total ≈");
        text.Should().Contain("+ field1");
        text.Should().Contain("- field2");
        text.Should().Contain("+ 2*field5/((field6)+eps)");
        text.Should().NotContain("field3");
        text.Should().NotContain("1*field1");
        text.Should().NotContain("*1");
    }

    [Fact]
    public void TopKFeatureSuggester_ShouldUseSameTypeCountExcludingTargetAndRowId()
    {
        var suggester = new TopKFeatureSuggester();
        var columns = new List<ColumnIndex>
        {
            new() { Name = "total", InferredType = InferredType.Money },
            new() { Name = "cost", InferredType = InferredType.Money },
            new() { Name = "margin", InferredType = InferredType.Money },
            new() { Name = "__row_id", InferredType = InferredType.Integer },
            new() { Name = "units", InferredType = InferredType.Integer }
        };

        var suggested = suggester.Suggest(columns, "total", maxK: 20);
        suggested.Should().Be(2);
    }

    [Fact]
    public void TopKFeatureSuggester_ShouldRespectMaxCap()
    {
        var suggester = new TopKFeatureSuggester();
        var columns = new List<ColumnIndex> { new() { Name = "target", InferredType = InferredType.Decimal } };
        columns.AddRange(Enumerable.Range(1, 12).Select(i => new ColumnIndex
        {
            Name = $"metric_{i}",
            InferredType = InferredType.Decimal
        }));

        var suggested = suggester.Suggest(columns, "target", maxK: 5);
        suggested.Should().Be(5);
    }
}
