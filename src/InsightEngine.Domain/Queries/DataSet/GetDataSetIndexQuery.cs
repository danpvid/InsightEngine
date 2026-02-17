using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexQuery : Query<DatasetIndex>
{
    public Guid DatasetId { get; set; }

    public GetDataSetIndexQuery(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}
