using InsightEngine.Domain.Core;
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
    public string? YColumn { get; set; }
    public string? GroupBy { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();

    public GetDataSetChartQuery() { }

    public GetDataSetChartQuery(
        Guid datasetId,
        string recommendationId,
        string? aggregation = null,
        string? timeBin = null,
        string? yColumn = null,
        string? groupBy = null,
        List<ChartFilter>? filters = null)
    {
        DatasetId = datasetId;
        RecommendationId = recommendationId;
        Aggregation = aggregation;
        TimeBin = timeBin;
        YColumn = yColumn;
        GroupBy = groupBy;
        Filters = filters ?? new List<ChartFilter>();
    }
}
