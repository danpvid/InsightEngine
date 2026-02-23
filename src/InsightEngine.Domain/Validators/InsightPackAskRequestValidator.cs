using FluentValidation;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Validators;

public class InsightPackAskRequestValidator : AbstractValidator<InsightPackAskRequest>
{
    public InsightPackAskRequestValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");

        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .WithMessage("RecommendationId is required.")
            .MaximumLength(128);

        RuleFor(x => x.Question)
            .NotEmpty()
            .WithMessage("Question is required.")
            .MaximumLength(1200);

        RuleFor(x => x.Language)
            .NotEmpty()
            .MaximumLength(16);

        RuleFor(x => x.OutputMode)
            .NotEmpty()
            .Must(mode => mode.Equals("DeepDive", StringComparison.OrdinalIgnoreCase)
                          || mode.Equals("Executive", StringComparison.OrdinalIgnoreCase))
            .WithMessage("OutputMode must be DeepDive or Executive.");
    }
}
