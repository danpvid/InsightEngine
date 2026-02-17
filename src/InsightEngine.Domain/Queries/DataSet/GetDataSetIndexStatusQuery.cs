using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetIndexStatusQuery : Query<DatasetIndexStatus>
{
    public Guid DatasetId { get; set; }

    public GetDataSetIndexStatusQuery(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}
