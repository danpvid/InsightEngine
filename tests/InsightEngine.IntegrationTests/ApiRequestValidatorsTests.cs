using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.API.Validators;
using InsightEngine.Application.Models.DataSet;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class ApiRequestValidatorsTests
{
    [Fact]
    public void FormulaInferenceRunRequestValidator_ShouldFail_WhenManualWithoutExpression()
    {
        var validator = new FormulaInferenceRunRequestValidator();
        var request = new FormulaInferenceRunRequest
        {
            TargetColumn = "sales",
            Mode = "Manual",
            ManualExpression = " "
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BuildIndexRequestValidator_ShouldFail_WhenSampleRowsOutOfRange()
    {
        var validator = new BuildIndexRequestValidator();
        var request = new BuildIndexRequest
        {
            MaxColumnsForCorrelation = 20,
            TopKEdgesPerColumn = 5,
            SampleRows = 50
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void FinalizeImportRequestValidator_ShouldFail_WhenTooManyIgnoredColumns()
    {
        var validator = new FinalizeImportRequestValidator();
        var request = new FinalizeImportRequest
        {
            TargetColumn = "sales",
            IgnoredColumns = Enumerable.Range(1, 201).Select(i => $"col_{i}").ToList()
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void FinalizeImportRequestValidator_ShouldPass_WithValidPayload()
    {
        var validator = new FinalizeImportRequestValidator();
        var request = new FinalizeImportRequest
        {
            ImportMode = "WithIndex",
            TargetColumn = "sales",
            IgnoredColumns = ["id"],
            ColumnTypeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sales"] = "Money"
            },
            CurrencyCode = "BRL"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
}
