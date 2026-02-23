using FluentValidation;
using InsightEngine.API.Models;
using InsightEngine.Domain.Enums;

namespace InsightEngine.API.Validators;

public class AiChartRequestValidator : AbstractValidator<AiChartRequest>
{
    public AiChartRequestValidator()
    {
        RuleFor(x => x.Filters)
            .Must(filters => filters.Count <= 3)
            .WithMessage("No more than 3 filters are allowed.");

        RuleForEach(x => x.Filters)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.Aggregation)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Aggregation));

        RuleFor(x => x.TimeBin)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeBin));

        RuleFor(x => x.MetricY)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.MetricY));

        RuleFor(x => x.GroupBy)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.GroupBy));
    }
}

public class DeepInsightsApiRequestValidator : AbstractValidator<DeepInsightsApiRequest>
{
    public DeepInsightsApiRequestValidator()
    {
        RuleFor(x => x.Filters)
            .Must(filters => filters.Count <= 3)
            .WithMessage("No more than 3 filters are allowed.");

        RuleForEach(x => x.Filters)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.Aggregation)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Aggregation));

        RuleFor(x => x.TimeBin)
            .MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeBin));

        RuleFor(x => x.MetricY)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.MetricY));

        RuleFor(x => x.GroupBy)
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.GroupBy));

        RuleFor(x => x.Horizon)
            .InclusiveBetween(1, 365)
            .When(x => x.Horizon.HasValue)
            .WithMessage("Horizon must be between 1 and 365.");
    }
}

public class InsightPackRequestValidator : AbstractValidator<InsightPackRequest>
{
    public InsightPackRequestValidator()
    {
        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.Filters)
            .Must(filters => filters.Count <= 3)
            .WithMessage("No more than 3 filters are allowed.");

        RuleForEach(x => x.Filters)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.OutputMode)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x.Horizon)
            .InclusiveBetween(1, 365)
            .When(x => x.Horizon.HasValue)
            .WithMessage("Horizon must be between 1 and 365.");

        RuleFor(x => x)
            .Must(x => !x.DateFrom.HasValue || !x.DateTo.HasValue || x.DateFrom <= x.DateTo)
            .WithMessage("DateFrom must be less than or equal to DateTo.");
    }
}

public class InsightPackAskApiRequestValidator : AbstractValidator<InsightPackAskApiRequest>
{
    public InsightPackAskApiRequestValidator()
    {
        Include(new InsightPackRequestValidator());

        RuleFor(x => x.Question)
            .NotEmpty()
            .MaximumLength(2000);
    }
}

public class InsightPackQueryRequestValidator : AbstractValidator<InsightPackQueryRequest>
{
    public InsightPackQueryRequestValidator()
    {
        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.Filters)
            .Must(filters => filters.Length <= 3)
            .WithMessage("No more than 3 filters are allowed.");

        RuleForEach(x => x.Filters)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.OutputMode)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x.Horizon)
            .InclusiveBetween(1, 365)
            .When(x => x.Horizon.HasValue)
            .WithMessage("Horizon must be between 1 and 365.");

        RuleFor(x => x)
            .Must(x => !x.DateFrom.HasValue || !x.DateTo.HasValue || x.DateFrom <= x.DateTo)
            .WithMessage("DateFrom must be less than or equal to DateTo.");
    }
}

public class AskDatasetRequestValidator : AbstractValidator<AskDatasetRequest>
{
    public AskDatasetRequestValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty()
            .MaximumLength(2000);
    }
}

public class ChartExecutionQueryRequestValidator : AbstractValidator<ChartExecutionQueryRequest>
{
    public ChartExecutionQueryRequestValidator()
    {
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

        RuleForEach(x => x.Filters)
            .NotEmpty()
            .MaximumLength(300);

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

public class ScenarioSimulationRequestValidator : AbstractValidator<ScenarioSimulationRequest>
{
    public ScenarioSimulationRequestValidator()
    {
        RuleFor(x => x.TargetMetric)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.TargetDimension)
            .NotEmpty()
            .MaximumLength(128);

        RuleFor(x => x.Operations)
            .NotEmpty()
            .WithMessage("At least one operation is required.")
            .Must(ops => ops.Count <= 20)
            .WithMessage("No more than 20 operations are allowed.");

        RuleForEach(x => x.Operations).ChildRules(operation =>
        {
            operation.RuleFor(op => op.Values)
                .Must(values => values.Count <= 50)
                .WithMessage("Operation values cannot exceed 50 items.");
        });

        RuleFor(x => x.Filters)
            .Must(filters => filters.Count <= 3)
            .WithMessage("No more than 3 filters are allowed.");
    }
}
