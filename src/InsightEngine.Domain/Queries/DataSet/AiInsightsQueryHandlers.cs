using InsightEngine.Domain.Core;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using MediatR;

namespace InsightEngine.Domain.Queries.DataSet;

public class GenerateAiSummaryQueryHandler : IRequestHandler<GenerateAiSummaryQuery, Result<AiInsightSummaryResult>>
{
    private readonly IAIInsightService _aiInsightService;
    private readonly IChartFilterParser _chartFilterParser;

    public GenerateAiSummaryQueryHandler(IAIInsightService aiInsightService, IChartFilterParser chartFilterParser)
    {
        _aiInsightService = aiInsightService;
        _chartFilterParser = chartFilterParser;
    }

    public async Task<Result<AiInsightSummaryResult>> Handle(GenerateAiSummaryQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<AiInsightSummaryResult>(parsedFilters.Errors);
        }

        return await _aiInsightService.GenerateAiSummaryAsync(new LLMChartContextRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            Language = request.Language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = parsedFilters.Data ?? [],
            ScenarioMeta = request.ScenarioMeta
        }, cancellationToken);
    }
}

public class ExplainChartQueryHandler : IRequestHandler<ExplainChartQuery, Result<ChartExplanationResult>>
{
    private readonly IAIInsightService _aiInsightService;
    private readonly IChartFilterParser _chartFilterParser;

    public ExplainChartQueryHandler(IAIInsightService aiInsightService, IChartFilterParser chartFilterParser)
    {
        _aiInsightService = aiInsightService;
        _chartFilterParser = chartFilterParser;
    }

    public async Task<Result<ChartExplanationResult>> Handle(ExplainChartQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<ChartExplanationResult>(parsedFilters.Errors);
        }

        return await _aiInsightService.ExplainChartAsync(new LLMChartContextRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            Language = request.Language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = parsedFilters.Data ?? [],
            ScenarioMeta = request.ScenarioMeta
        }, cancellationToken);
    }
}

public class GenerateDeepInsightsQueryHandler : IRequestHandler<GenerateDeepInsightsQuery, Result<DeepInsightsResult>>
{
    private readonly IAIInsightService _aiInsightService;
    private readonly IChartFilterParser _chartFilterParser;

    public GenerateDeepInsightsQueryHandler(IAIInsightService aiInsightService, IChartFilterParser chartFilterParser)
    {
        _aiInsightService = aiInsightService;
        _chartFilterParser = chartFilterParser;
    }

    public async Task<Result<DeepInsightsResult>> Handle(GenerateDeepInsightsQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<DeepInsightsResult>(parsedFilters.Errors);
        }

        return await _aiInsightService.GenerateDeepInsightsAsync(new DeepInsightsRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            Language = request.Language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = parsedFilters.Data ?? [],
            Scenario = request.Scenario,
            Horizon = request.Horizon,
            SensitiveMode = request.SensitiveMode,
            RequesterKey = request.RequesterKey
        }, cancellationToken);
    }
}

public class BuildInsightPackQueryHandler : IRequestHandler<BuildInsightPackQuery, Result<SemanticInsightPackResult>>
{
    private readonly IAIInsightService _aiInsightService;
    private readonly IChartFilterParser _chartFilterParser;

    public BuildInsightPackQueryHandler(IAIInsightService aiInsightService, IChartFilterParser chartFilterParser)
    {
        _aiInsightService = aiInsightService;
        _chartFilterParser = chartFilterParser;
    }

    public async Task<Result<SemanticInsightPackResult>> Handle(BuildInsightPackQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<SemanticInsightPackResult>(parsedFilters.Errors);
        }

        return await _aiInsightService.BuildSemanticInsightPackAsync(new DeepInsightsRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            Language = request.Language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = parsedFilters.Data ?? [],
            Month = request.Month,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            SegmentColumn = request.SegmentColumn,
            SegmentValue = request.SegmentValue,
            OutputMode = request.OutputMode,
            Scenario = request.Scenario,
            Horizon = request.Horizon,
            SensitiveMode = request.SensitiveMode,
            RequesterKey = request.RequesterKey
        }, cancellationToken);
    }
}

public class AskWithInsightPackQueryHandler : IRequestHandler<AskWithInsightPackQuery, Result<InsightPackAskResult>>
{
    private readonly IAIInsightService _aiInsightService;
    private readonly IChartFilterParser _chartFilterParser;

    public AskWithInsightPackQueryHandler(IAIInsightService aiInsightService, IChartFilterParser chartFilterParser)
    {
        _aiInsightService = aiInsightService;
        _chartFilterParser = chartFilterParser;
    }

    public async Task<Result<InsightPackAskResult>> Handle(AskWithInsightPackQuery request, CancellationToken cancellationToken)
    {
        var parsedFilters = _chartFilterParser.Parse(request.Filters);
        if (!parsedFilters.IsSuccess)
        {
            return Result.Failure<InsightPackAskResult>(parsedFilters.Errors);
        }

        return await _aiInsightService.AskWithInsightPackAsync(new InsightPackAskRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            Question = request.Question,
            Language = request.Language,
            Aggregation = request.Aggregation,
            TimeBin = request.TimeBin,
            MetricY = request.MetricY,
            GroupBy = request.GroupBy,
            Filters = parsedFilters.Data ?? [],
            Month = request.Month,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            SegmentColumn = request.SegmentColumn,
            SegmentValue = request.SegmentValue,
            OutputMode = request.OutputMode,
            SensitiveMode = request.SensitiveMode
        }, cancellationToken);
    }
}

public class AskDatasetAnalysisPlanQueryHandler : IRequestHandler<AskDatasetAnalysisPlanQuery, Result<AskAnalysisPlanResult>>
{
    private readonly IAIInsightService _aiInsightService;

    public AskDatasetAnalysisPlanQueryHandler(IAIInsightService aiInsightService)
    {
        _aiInsightService = aiInsightService;
    }

    public async Task<Result<AskAnalysisPlanResult>> Handle(AskDatasetAnalysisPlanQuery request, CancellationToken cancellationToken)
    {
        return await _aiInsightService.AskAnalysisPlanAsync(new AskAnalysisPlanRequest
        {
            DatasetId = request.DatasetId,
            Language = request.Language,
            Question = request.Question,
            CurrentView = request.CurrentView
        }, cancellationToken);
    }
}
