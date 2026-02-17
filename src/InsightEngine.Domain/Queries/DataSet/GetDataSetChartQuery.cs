using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Query para executar uma recomendação de gráfico e retornar resposta completa com telemetria
/// </summary>
public class GetDataSetChartQuery : Query<ChartExecutionResponse>
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;

    // Parâmetros opcionais para controles dinâmicos (frontend)
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? XColumn { get; set; }
    public string? YColumn { get; set; }
    public string? GroupBy { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
    public ChartViewKind View { get; set; } = ChartViewKind.Base;
    public PercentileMode PercentileMode { get; set; } = PercentileMode.None;
    public PercentileKind? PercentileKind { get; set; }
    public string PercentileTarget { get; set; } = "y";

    public GetDataSetChartQuery() { }

    public GetDataSetChartQuery(
        Guid datasetId,
        string recommendationId,
        string? aggregation = null,
        string? timeBin = null,
        string? yColumn = null,
        string? groupBy = null,
        List<ChartFilter>? filters = null,
        ChartViewKind view = ChartViewKind.Base,
        PercentileMode percentileMode = PercentileMode.None,
        PercentileKind? percentileKind = null,
        string? percentileTarget = null,
        string? xColumn = null)
    {
        DatasetId = datasetId;
        RecommendationId = recommendationId;
        Aggregation = aggregation;
        TimeBin = timeBin;
        XColumn = xColumn;
        YColumn = yColumn;
        GroupBy = groupBy;
        Filters = filters ?? new List<ChartFilter>();
        View = view;
        PercentileMode = percentileMode;
        PercentileKind = percentileKind;
        PercentileTarget = string.IsNullOrWhiteSpace(percentileTarget) ? "y" : percentileTarget!;
    }
}
