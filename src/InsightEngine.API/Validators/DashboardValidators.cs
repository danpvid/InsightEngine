using FluentValidation;
using InsightEngine.API.Models;

namespace InsightEngine.API.Validators;

public class GetDashboardRequestValidator : AbstractValidator<GetDashboardRequest>
{
    public GetDashboardRequestValidator()
    {
        RuleFor(request => request.DatasetId)
            .NotEmpty()
            .WithMessage("datasetId is required.");
    }
}
