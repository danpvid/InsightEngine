using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexStatusQueryValidator : AbstractValidator<GetDataSetIndexStatusQuery>
{
    public GetDataSetIndexStatusQueryValidator()
    {
        RuleFor(query => query.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");
    }
}
