using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Query to get chart recommendations for a dataset
/// </summary>
public class GetDataSetRecommendationsQuery : Query<List<ChartRecommendation>>
{
    public Guid DatasetId { get; set; }
    public int MaxRecommendations { get; set; } = 12;

    public GetDataSetRecommendationsQuery(Guid datasetId, int maxRecommendations = 12)
    {
        DatasetId = datasetId;
        MaxRecommendations = maxRecommendations;
    }
}
