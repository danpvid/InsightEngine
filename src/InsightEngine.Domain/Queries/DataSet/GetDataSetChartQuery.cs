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

    public GetDataSetChartQuery() { }

    public GetDataSetChartQuery(Guid datasetId, string recommendationId)
    {
        DatasetId = datasetId;
        RecommendationId = recommendationId;
    }
}
