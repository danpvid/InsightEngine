using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Validator for GetDataSetRecommendationsQuery
/// </summary>
public class GetDataSetRecommendationsQueryValidator : AbstractValidator<GetDataSetRecommendationsQuery>
{
    public GetDataSetRecommendationsQueryValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId cannot be empty");

        RuleFor(x => x.MaxRecommendations)
            .GreaterThan(0)
            .WithMessage("MaxRecommendations must be greater than 0")
            .LessThanOrEqualTo(50)
            .WithMessage("MaxRecommendations cannot exceed 50");
    }
}
