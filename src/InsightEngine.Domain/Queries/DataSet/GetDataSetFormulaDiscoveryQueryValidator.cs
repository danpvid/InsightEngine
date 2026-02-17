using FluentValidation;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetFormulaDiscoveryQueryValidator : AbstractValidator<GetDataSetFormulaDiscoveryQuery>
{
    private readonly IIndexStore _indexStore;

    public GetDataSetFormulaDiscoveryQueryValidator(IIndexStore indexStore)
    {
        _indexStore = indexStore;

        RuleFor(query => query.DatasetId)
            .NotEmpty()
            .WithMessage("DatasetId is required.");

        RuleFor(query => query.MaxCandidates)
            .InclusiveBetween(1, 5)
            .WithMessage("MaxCandidates must be between 1 and 5.");

        RuleFor(query => query.TopKFeatures)
            .InclusiveBetween(3, 20)
            .WithMessage("TopKFeatures must be between 3 and 20.");

        RuleFor(query => query.SampleCap)
            .InclusiveBetween(1000, 100000)
            .WithMessage("SampleCap must be between 1000 and 100000.");

        RuleFor(query => query.Target)
            .MaximumLength(128)
            .When(query => !string.IsNullOrWhiteSpace(query.Target))
            .WithMessage("Target must not exceed 128 characters.");

        RuleFor(query => query)
            .MustAsync(TargetColumnExistsAndIsNumericAsync)
            .When(query => !string.IsNullOrWhiteSpace(query.Target))
            .WithMessage("Target column must exist and be numeric.");
    }

    private async Task<bool> TargetColumnExistsAndIsNumericAsync(
        GetDataSetFormulaDiscoveryQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Target))
        {
            return true;
        }

        var index = await _indexStore.LoadAsync(query.DatasetId, cancellationToken);
        if (index == null)
        {
            // Allow handler to resolve when metadata index is not available yet.
            return true;
        }

        var target = index.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, query.Target, StringComparison.OrdinalIgnoreCase));

        return target != null && target.InferredType == InferredType.Number;
    }
}
