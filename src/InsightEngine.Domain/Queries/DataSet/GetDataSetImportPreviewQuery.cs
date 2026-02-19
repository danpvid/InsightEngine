using InsightEngine.Domain.Models.ImportPreview;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetImportPreviewQuery : Query<ImportPreviewResponse>
{
    public Guid DatasetId { get; }
    public int SampleSize { get; }

    public GetDataSetImportPreviewQuery(Guid datasetId, int sampleSize = 200)
    {
        DatasetId = datasetId;
        SampleSize = sampleSize;
    }
}
