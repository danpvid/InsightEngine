using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Validador para GetDataSetChartQuery
/// </summary>
public class GetDataSetChartQueryValidator : AbstractValidator<GetDataSetChartQuery>
{
    public GetDataSetChartQueryValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required");

        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .WithMessage("RecommendationId is required")
            .MaximumLength(50)
            .WithMessage("RecommendationId must not exceed 50 characters")
            .Matches(@"^rec_\d{3}$")
            .WithMessage("RecommendationId must follow pattern 'rec_###' (e.g., rec_001)");
    }
}
