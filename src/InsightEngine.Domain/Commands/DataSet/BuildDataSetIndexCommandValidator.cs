using FluentValidation;

namespace InsightEngine.Domain.Commands.DataSet;

public class BuildDataSetIndexCommandValidator : AbstractValidator<BuildDataSetIndexCommand>
{
    public BuildDataSetIndexCommandValidator()
    {
        RuleFor(command => command.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");

        RuleFor(command => command.MaxColumnsForCorrelation)
            .InclusiveBetween(2, 50)
            .WithMessage("MaxColumnsForCorrelation must be between 2 and 50.");

        RuleFor(command => command.TopKEdgesPerColumn)
            .InclusiveBetween(1, 20)
            .WithMessage("TopKEdgesPerColumn must be between 1 and 20.");

        RuleFor(command => command.SampleRows)
            .InclusiveBetween(1000, 100000)
            .WithMessage("SampleRows must be between 1000 and 100000.");
    }
}
