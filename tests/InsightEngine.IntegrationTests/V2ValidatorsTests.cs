using FluentAssertions;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Validators;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class V2ValidatorsTests
{
    [Fact]
    public void DeepInsightsRequestValidator_ShouldFail_WhenDatasetIdIsEmpty()
    {
        var validator = new DeepInsightsRequestValidator();
        var request = new DeepInsightsRequest
        {
            DatasetId = Guid.Empty,
            RecommendationId = "rec_001",
            Language = "pt-br"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void DeepInsightsRequestValidator_ShouldFail_WhenOutputModeIsInvalid()
    {
        var validator = new DeepInsightsRequestValidator();
        var request = new DeepInsightsRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Language = "pt-br",
            OutputMode = "invalid"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InsightPackAskRequestValidator_ShouldFail_WhenQuestionIsMissing()
    {
        var validator = new InsightPackAskRequestValidator();
        var request = new InsightPackAskRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Question = ""
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InsightPackAskRequestValidator_ShouldPass_WithMinimalValidPayload()
    {
        var validator = new InsightPackAskRequestValidator();
        var request = new InsightPackAskRequest
        {
            DatasetId = Guid.NewGuid(),
            RecommendationId = "rec_001",
            Question = "Quais drivers principais?",
            Language = "pt-br",
            OutputMode = "DeepDive"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
}
