using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDashboardQueryValidator : AbstractValidator<GetDashboardQuery>
{
    public GetDashboardQueryValidator()
    {
        RuleFor(query => query.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId cannot be empty.");
    }
}
