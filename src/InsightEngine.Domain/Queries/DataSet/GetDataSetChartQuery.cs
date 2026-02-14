using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Query para executar uma recomendação de gráfico e retornar EChartsOption completo
/// </summary>
public class GetDataSetChartQuery : Query<EChartsOption>
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
