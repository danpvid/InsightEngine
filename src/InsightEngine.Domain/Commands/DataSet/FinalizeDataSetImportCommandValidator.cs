using FluentValidation;

namespace InsightEngine.Domain.Commands.DataSet;

public class FinalizeDataSetImportCommandValidator : AbstractValidator<FinalizeDataSetImportCommand>
{
    public FinalizeDataSetImportCommandValidator()
    {
        RuleFor(command => command.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");

        RuleFor(command => command.CurrencyCode)
            .NotEmpty()
            .MaximumLength(8)
            .WithMessage("CurrencyCode must be provided and have up to 8 characters.");

        RuleFor(command => command.IgnoredColumns)
            .NotNull();

        RuleFor(command => command.ColumnTypeOverrides)
            .NotNull();

        RuleFor(command => command.IgnoredColumns)
            .Must(columns => !columns.Any(column => string.Equals(column, "_id", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Ignored columns cannot include canonical _id.");

        RuleFor(command => command.UniqueKeyColumn)
            .MaximumLength(256)
            .When(command => !string.IsNullOrWhiteSpace(command.UniqueKeyColumn));
    }
}
