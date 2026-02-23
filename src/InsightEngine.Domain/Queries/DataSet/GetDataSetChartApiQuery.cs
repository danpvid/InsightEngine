using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using MediatR;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetChartApiQuery : Query<ChartExecutionResponse>
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? XColumn { get; set; }
    public string? YColumn { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public string[] Filters { get; set; } = [];
    public string? View { get; set; }
    public string? Percentile { get; set; }
    public string? Mode { get; set; }
    public string? PercentileTarget { get; set; }
}

public class GetDataSetChartApiQueryHandler : IRequestHandler<GetDataSetChartApiQuery, Result<ChartExecutionResponse>>
{
    private readonly IChartFilterParser _chartFilterParser;
    private readonly IMediator _mediator;

    public GetDataSetChartApiQueryHandler(IChartFilterParser chartFilterParser, IMediator mediator)
    {
        _chartFilterParser = chartFilterParser;
        _mediator = mediator;
    }

    public async Task<Result<ChartExecutionResponse>> Handle(GetDataSetChartApiQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<ChartExecutionResponse>(parsedFilters.Errors);
        }

        var resolvedMetricY = !string.IsNullOrWhiteSpace(request.MetricY) ? request.MetricY : request.YColumn;
        var resolvedView = Enum.TryParse<ChartViewKind>(request.View, true, out var view) ? view : ChartViewKind.Base;
        var resolvedMode = Enum.TryParse<PercentileMode>(request.Mode, true, out var mode) ? mode : PercentileMode.None;
        PercentileKind? resolvedPercentile = null;
        if (!string.IsNullOrWhiteSpace(request.Percentile)
            && Enum.TryParse<PercentileKind>(request.Percentile, true, out var percentile))
        {
            resolvedPercentile = percentile;
        }

        return await _mediator.Send(new GetDataSetChartQuery(
            request.DatasetId,
            request.RecommendationId,
            request.Aggregation,
            request.TimeBin,
            resolvedMetricY,
            request.GroupBy,
            parsedFilters.Data ?? [],
            resolvedView,
            resolvedMode,
            resolvedPercentile,
            request.PercentileTarget,
            request.XColumn), cancellationToken);
    }
}
