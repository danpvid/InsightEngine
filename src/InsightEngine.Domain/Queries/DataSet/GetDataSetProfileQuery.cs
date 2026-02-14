using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Query to get dataset profile with column analysis
/// </summary>
public class GetDataSetProfileQuery : Query<DatasetProfile>
{
    public Guid DatasetId { get; set; }

    public GetDataSetProfileQuery(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}
