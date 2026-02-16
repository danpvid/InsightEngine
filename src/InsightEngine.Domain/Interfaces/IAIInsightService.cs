using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IAIInsightService
{
    Task<Result<AiInsightSummaryResult>> GenerateAiSummaryAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ChartExplanationResult>> ExplainChartAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AskAnalysisPlanResult>> AskAnalysisPlanAsync(
        AskAnalysisPlanRequest request,
        CancellationToken cancellationToken = default);
}
