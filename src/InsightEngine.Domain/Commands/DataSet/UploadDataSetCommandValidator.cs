using FluentValidation;

namespace InsightEngine.Domain.Commands.DataSet;

/// <summary>
/// Validator for UploadDataSetCommand
/// </summary>
public class UploadDataSetCommandValidator : AbstractValidator<UploadDataSetCommand>
{
    public UploadDataSetCommandValidator()
    {
        RuleFor(x => x.File)
            .NotNull()
            .WithMessage("No file was provided")
            .Must(file => file != null && file.Length > 0)
            .WithMessage("File is empty")
            .Must(file => file != null && Path.GetExtension(file.FileName).ToLowerInvariant() == ".csv")
            .WithMessage("Only CSV files are allowed");

        RuleFor(x => x.File.Length)
            .LessThanOrEqualTo(x => x.MaxFileSizeBytes)
            .When(x => x.File != null)
            .WithMessage(x => $"File size exceeds the maximum allowed size of {x.MaxFileSizeBytes / (1024 * 1024)}MB");

        RuleFor(x => x.File.FileName)
            .NotEmpty()
            .When(x => x.File != null)
            .WithMessage("Filename cannot be empty");
    }
}
