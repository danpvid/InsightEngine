using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Validator for GetDataSetProfileQuery
/// </summary>
public class GetDataSetProfileQueryValidator : AbstractValidator<GetDataSetProfileQuery>
{
    public GetDataSetProfileQueryValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId cannot be empty");
    }
}
