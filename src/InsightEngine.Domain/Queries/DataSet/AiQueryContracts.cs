namespace InsightEngine.Domain.Queries.DataSet;

public interface IAiQueryBase
{
    Guid DatasetId { get; }
    string RecommendationId { get; }
    string Language { get; }
    string[] Filters { get; }
}
