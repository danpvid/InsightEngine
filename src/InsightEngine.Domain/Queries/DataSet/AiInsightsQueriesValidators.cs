using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

public class GenerateAiSummaryQueryValidator : AbstractValidator<GenerateAiSummaryQuery>
{
    public GenerateAiSummaryQueryValidator()
    {
        Include(new AiQueryBaseValidator<GenerateAiSummaryQuery>());
    }
}

public class ExplainChartQueryValidator : AbstractValidator<ExplainChartQuery>
{
    public ExplainChartQueryValidator()
    {
        Include(new AiQueryBaseValidator<ExplainChartQuery>());
    }
}

public class GenerateDeepInsightsQueryValidator : AbstractValidator<GenerateDeepInsightsQuery>
{
    public GenerateDeepInsightsQueryValidator()
    {
        Include(new AiQueryBaseValidator<GenerateDeepInsightsQuery>());

        RuleFor(x => x.Horizon)
            .InclusiveBetween(1, 365)
            .When(x => x.Horizon.HasValue)
            .WithMessage("Horizon must be between 1 and 365.");

        RuleFor(x => x.RequesterKey)
            .NotEmpty()
            .MaximumLength(200);
    }
}

public class BuildInsightPackQueryValidator : AbstractValidator<BuildInsightPackQuery>
{
    public BuildInsightPackQueryValidator()
    {
        Include(new AiQueryBaseValidator<BuildInsightPackQuery>());

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

public class AskWithInsightPackQueryValidator : AbstractValidator<AskWithInsightPackQuery>
{
    public AskWithInsightPackQueryValidator()
    {
        Include(new AiQueryBaseValidator<AskWithInsightPackQuery>());

        RuleFor(x => x.Question)
            .NotEmpty()
            .MaximumLength(2000);

        RuleFor(x => x.OutputMode)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x)
            .Must(x => !x.DateFrom.HasValue || !x.DateTo.HasValue || x.DateFrom <= x.DateTo)
            .WithMessage("DateFrom must be less than or equal to DateTo.");
    }
}

public class AskDatasetAnalysisPlanQueryValidator : AbstractValidator<AskDatasetAnalysisPlanQuery>
{
    public AskDatasetAnalysisPlanQueryValidator()
    {
        RuleFor(x => x.DatasetId).NotEmpty();

        RuleFor(x => x.Language)
            .NotEmpty()
            .Must(language => language is "pt-br" or "en")
            .WithMessage("Language must be 'pt-br' or 'en'.");

        RuleFor(x => x.Question)
            .NotEmpty()
            .MaximumLength(2000);
    }
}

internal class AiQueryBaseValidator<T> : AbstractValidator<T> where T : IAiQueryBase
{
    public AiQueryBaseValidator()
    {
        RuleFor(x => x.DatasetId)
            .NotEmpty();

        RuleFor(x => x.RecommendationId)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(x => x.Language)
            .NotEmpty()
            .Must(language => language is "pt-br" or "en")
            .WithMessage("Language must be 'pt-br' or 'en'.");

        RuleFor(x => x.Filters)
            .Must(filters => filters.Length <= 3)
            .WithMessage("No more than 3 filters are allowed.");
    }
}
