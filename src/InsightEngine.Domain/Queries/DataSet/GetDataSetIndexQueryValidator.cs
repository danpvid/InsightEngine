using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexQueryValidator : AbstractValidator<GetDataSetIndexQuery>
{
    public GetDataSetIndexQueryValidator()
    {
        RuleFor(query => query.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");
    }
}
