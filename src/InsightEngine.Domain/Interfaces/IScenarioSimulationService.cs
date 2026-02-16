using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Interfaces;

public interface IScenarioSimulationService
{
    Task<Result<ScenarioSimulationResponse>> SimulateAsync(
        Guid datasetId,
        DatasetProfile profile,
        ScenarioRequest request,
        CancellationToken ct = default);
}
