using FluentValidation;
using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetChartApiQueryValidator : AbstractValidator<GetDataSetChartApiQuery>
{
    public GetDataSetChartApiQueryValidator()
    {
        RuleFor(x => x.DatasetId).NotEmpty();
        RuleFor(x => x.RecommendationId).NotEmpty().MaximumLength(80);

        RuleFor(x => x.Aggregation)
            .Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<Aggregation>(value, true, out _))
            .WithMessage("Aggregation must be one of: Sum, Avg, Count, Min, Max.");

        RuleFor(x => x.TimeBin)
            .Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<TimeBin>(value, true, out _))
            .WithMessage("TimeBin must be one of: Day, Week, Month, Quarter, Year.");

        RuleFor(x => x.View)
            .Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<ChartViewKind>(value, true, out _))
            .WithMessage("View must be one of: Base, Percentile.");

        RuleFor(x => x.Mode)
            .Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<PercentileMode>(value, true, out _))
            .WithMessage("Mode must be one of: None, Bucket, Overall.");

        RuleFor(x => x.Percentile)
            .Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<PercentileKind>(value, true, out _))
            .WithMessage("Percentile must be one of: P5, P10, P90, P95.");

        RuleFor(x => x.Filters)
            .Must(filters => filters.Length <= 3)
            .WithMessage("No more than 3 filters are allowed.");

        RuleFor(x => x)
            .Must(x =>
            {
                if (!Enum.TryParse<ChartViewKind>(x.View, true, out var parsedView))
                {
                    return true;
                }

                return parsedView != ChartViewKind.Percentile || !string.IsNullOrWhiteSpace(x.Percentile);
            })
            .WithMessage("Percentile view requires percentile=P5|P10|P90|P95.");
    }
}

public class SimulateDataSetQueryValidator : AbstractValidator<SimulateDataSetQuery>
{
    public SimulateDataSetQueryValidator()
    {
        RuleFor(x => x.DatasetId).NotEmpty();
        RuleFor(x => x.Request).NotNull();
    }
}
