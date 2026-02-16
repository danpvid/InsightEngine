using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IAIInsightService
{
    Task<Result<AiInsightSummaryResult>> GenerateAiSummaryAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default);
}
