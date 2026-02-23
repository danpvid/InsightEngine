using FluentValidation;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Validators;

public class DeepInsightsRequestValidator : AbstractValidator<DeepInsightsRequest>
{
    public DeepInsightsRequestValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");

        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .WithMessage("RecommendationId is required.")
            .MaximumLength(128);

        RuleFor(x => x.Language)
            .NotEmpty()
            .MaximumLength(16);

        RuleFor(x => x.OutputMode)
            .NotEmpty()
            .Must(mode => mode.Equals("DeepDive", StringComparison.OrdinalIgnoreCase)
                          || mode.Equals("Executive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("OutputMode must be DeepDive or Executive.");

        RuleFor(x => x.Horizon)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Horizon.HasValue)
            .WithMessage("Horizon must be >= 1.");

        RuleFor(x => x.Filters)
            .Must(filters => filters.Count <= 10)
            .WithMessage("Filters cannot exceed 10 items.");
    }
}
