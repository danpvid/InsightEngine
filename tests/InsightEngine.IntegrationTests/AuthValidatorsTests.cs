using FluentAssertions;
using InsightEngine.API.CQRS.Auth;
using InsightEngine.API.Models;
using InsightEngine.API.Validators;
using Xunit;

namespace InsightEngine.IntegrationTests;

public class AuthValidatorsTests
{
    [Fact]
    public void RegisterRequestValidator_ShouldFail_WhenPasswordTooShort()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest
        {
            Email = "valid@test.local",
            Password = "123",
            DisplayName = "Valid Name"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateAvatarRequestValidator_ShouldFail_WhenUrlIsInvalid()
    {
        var validator = new UpdateAvatarRequestValidator();
        var request = new UpdateAvatarRequest
        {
            AvatarUrl = "invalid-url"
        };

        var result = validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void LoginCommandValidator_ShouldPass_WithValidPayload()
    {
        var validator = new LoginCommandValidator();
        var command = new LoginCommand("valid@test.local", "StrongPass123!");

        var result = validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpgradeMePlanCommandValidator_ShouldFail_WhenTargetPlanEmpty()
    {
        var validator = new UpgradeMePlanCommandValidator();
        var command = new UpgradeMePlanCommand(string.Empty);

        var result = validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }
}
