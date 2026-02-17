using System;
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

        RuleFor(x => x.Aggregation)
            .Must(BeValidAggregation)
            .When(x => !string.IsNullOrWhiteSpace(x.Aggregation))
            .WithMessage("Aggregation must be one of: Sum, Avg, Count, Min, Max");

        RuleFor(x => x.TimeBin)
            .Must(BeValidTimeBin)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeBin))
            .WithMessage("TimeBin must be one of: Day, Week, Month, Quarter, Year");

        RuleFor(x => x.GroupBy)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.GroupBy))
            .WithMessage("GroupBy must not exceed 128 characters");

        RuleFor(x => x.XColumn)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.XColumn))
            .WithMessage("XColumn must not exceed 128 characters");

        RuleFor(x => x.Filters)
            .Must(filters => filters == null || filters.Count <= 3)
            .WithMessage("No more than 3 filters are allowed");
    }

    private static bool BeValidAggregation(string? aggregation)
    {
        return Enum.TryParse<InsightEngine.Domain.Enums.Aggregation>(aggregation, true, out _);
    }

    private static bool BeValidTimeBin(string? timeBin)
    {
        return Enum.TryParse<InsightEngine.Domain.Enums.TimeBin>(timeBin, true, out _);
    }
}
