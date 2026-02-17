using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IChartPercentileService
{
    Task<Result<ChartPercentileComputationResult>> ComputeAsync(
        string csvPath,
        ChartRecommendation recommendation,
        EChartsOption baseOption,
        ChartViewKind view,
        PercentileMode requestedMode,
        PercentileKind? percentileKind,
        string percentileTarget,
        CancellationToken cancellationToken = default);
}
