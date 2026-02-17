using InsightEngine.Domain.Models.FormulaDiscovery;

namespace InsightEngine.Domain.Interfaces;

public interface IFormulaDiscoveryService
{
    Task<FormulaDiscoveryResult> DiscoverAsync(
        Guid datasetId,
        string targetColumn,
        FormulaDiscoveryOptions options,
        CancellationToken cancellationToken = default);
}
