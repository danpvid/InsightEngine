using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface ILLMContextBuilder
{
    Task<Result<LLMContextPayload>> BuildChartContextAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<LLMContextPayload>> BuildAskContextAsync(
        LLMAskContextRequest request,
        CancellationToken cancellationToken = default);
}
