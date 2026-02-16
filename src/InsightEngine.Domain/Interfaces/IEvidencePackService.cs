using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IEvidencePackService
{
    Task<Result<EvidencePackResult>> BuildEvidencePackAsync(
        DeepInsightsRequest request,
        CancellationToken cancellationToken = default);
}
