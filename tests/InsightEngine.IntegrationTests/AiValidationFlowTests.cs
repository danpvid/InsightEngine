using FluentAssertions;
using InsightEngine.API.Models;
using InsightEngine.API.Validators;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Queries.DataSet;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class AiValidationFlowTests
{
    [Fact]
    public void DtoValidator_ShouldRejectTooManyFilters()
    {
        var validator = new AiChartRequestValidator();
        var dto = new AiChartRequest
        {
            Filters = ["a|Eq|1", "b|Eq|2", "c|Eq|3", "d|Eq|4"]
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("No more than 3 filters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DtoValidator_ShouldRejectEmptyQuestion()
    {
        var validator = new AskDatasetRequestValidator();
        var dto = new AskDatasetRequest { Question = "" };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(AskDatasetRequest.Question));
    }

    [Fact]
    public void QueryValidator_ShouldRejectInvalidLanguage()
    {
        var validator = new GenerateDeepInsightsQueryValidator();
        var query = new GenerateDeepInsightsQuery
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Language = "es",
            Filters = []
        };

        var result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("Language must be", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void QueryValidator_ShouldAcceptValidAskWithPack()
    {
        var validator = new AskWithInsightPackQueryValidator();
        var query = new AskWithInsightPackQuery
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Question = "What changed month over month?",
            Language = "pt-br",
            Filters = ["segment|Eq|A"],
            OutputMode = "DeepDive"
        };

        var result = validator.Validate(query);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ChartDtoValidator_ShouldRejectPercentileViewWithoutPercentile()
    {
        var validator = new ChartExecutionQueryRequestValidator();
        var dto = new ChartExecutionQueryRequest
        {
            View = "Percentile",
            Filters = []
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("Percentile view requires", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SimulationDtoValidator_ShouldRejectEmptyOperations()
    {
        var validator = new ScenarioSimulationRequestValidator();
        var dto = new ScenarioSimulationRequest
        {
            TargetMetric = "revenue",
            TargetDimension = "segment",
            Operations = []
        };

        var result = validator.Validate(dto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("At least one operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChartQueryValidator_ShouldRejectInvalidMode()
    {
        var validator = new GetDataSetChartApiQueryValidator();
        var query = new GetDataSetChartApiQuery
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Mode = "invalid-mode"
        };

        var result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("Mode must be", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SimulateQueryValidator_ShouldRejectMissingDatasetId()
    {
        var validator = new SimulateDataSetQueryValidator();
        var query = new SimulateDataSetQuery
        {
            DatasetId = Guid.Empty,
            Request = new ScenarioRequest
            {
                TargetMetric = "revenue",
                TargetDimension = "segment"
            }
        };

        var result = validator.Validate(query);

        result.IsValid.Should().BeFalse();
    }
}
