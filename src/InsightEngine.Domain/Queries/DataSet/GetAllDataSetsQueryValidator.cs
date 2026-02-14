using FluentValidation;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Validator for GetAllDataSetsQuery
/// </summary>
public class GetAllDataSetsQueryValidator : AbstractValidator<GetAllDataSetsQuery>
{
    public GetAllDataSetsQueryValidator()
    {
        // No validation rules needed - query has no parameters
    }
}
