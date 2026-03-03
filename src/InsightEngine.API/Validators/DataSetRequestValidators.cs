using FluentValidation;
using InsightEngine.API.Models;
using InsightEngine.Application.Models.DataSet;

namespace InsightEngine.API.Validators;

public class BuildIndexRequestValidator : AbstractValidator<BuildIndexRequest>
{
    public BuildIndexRequestValidator()
    {
        RuleFor(x => x.MaxColumnsForCorrelation)
            .InclusiveBetween(1, 500);

        RuleFor(x => x.TopKEdgesPerColumn)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.SampleRows)
            .InclusiveBetween(100, 1_000_000);
    }
}

public class FinalizeImportRequestValidator : AbstractValidator<FinalizeImportRequest>
{
    public FinalizeImportRequestValidator()
    {
        RuleFor(x => x.ImportMode)
            .Must(mode =>
            {
                if (string.IsNullOrWhiteSpace(mode))
                {
                    return true;
                }

                return mode.Trim().ToLowerInvariant() is "basic" or "withindex" or "with-index" or "standard";
            })
            .WithMessage("ImportMode must be 'Basic'/'standard' or 'WithIndex'/'with-index'.");

        RuleFor(x => x.TargetColumn)
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.TargetColumn));

        RuleFor(x => x.UniqueKeyColumn)
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.UniqueKeyColumn));

        RuleFor(x => x.IgnoredColumns)
            .Must(columns => columns.Count <= 200)
            .WithMessage("IgnoredColumns cannot exceed 200 items.");

        RuleForEach(x => x.IgnoredColumns)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.IgnoredColumns)
            .Must(columns => !columns.Any(column => string.Equals(column, "_id", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("IgnoredColumns cannot contain canonical _id.");

        RuleFor(x => x.ColumnTypeOverrides)
            .Must(overrides => overrides.Count <= 500)
            .WithMessage("ColumnTypeOverrides cannot exceed 500 entries.");

        RuleFor(x => x.CurrencyCode)
            .Length(3)
            .When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode));
    }
}

public class FormulaInferenceRunRequestValidator : AbstractValidator<FormulaInferenceRunRequest>
{
    public FormulaInferenceRunRequestValidator()
    {
        RuleFor(x => x.TargetColumn)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.Mode)
            .NotEmpty()
            .Must(mode => mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                          || mode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Mode must be 'Auto' or 'Manual'.");

        RuleFor(x => x.ManualExpression)
            .NotEmpty()
            .MaximumLength(2000)
            .When(x => x.Mode.Equals("Manual", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.Options!.MaxColumns)
            .InclusiveBetween(1, 200)
            .When(x => x.Options?.MaxColumns.HasValue == true);

        RuleFor(x => x.Options!.MaxDepth)
            .InclusiveBetween(1, 12)
            .When(x => x.Options?.MaxDepth.HasValue == true);

        RuleFor(x => x.Options!.EpsilonAbs)
            .GreaterThan(0)
            .When(x => x.Options?.EpsilonAbs.HasValue == true);
    }
}
