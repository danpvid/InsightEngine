using InsightEngine.Domain.Models.Formulas;
using InsightEngine.Domain.Settings;

namespace InsightEngine.Domain.Interfaces;

public interface IFormulaInferenceEngine
{
    Task<FormulaInferenceResult> InferAsync(
        Guid datasetId,
        string targetColumn,
        IReadOnlyCollection<string>? numericColumnsCandidate = null,
        FormulaInferenceSettings? settingsOverride = null,
        CancellationToken cancellationToken = default);
}
