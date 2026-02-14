using InsightEngine.Domain.Core;

namespace InsightEngine.Domain.Queries.DataSet;

/// <summary>
/// Query to retrieve all datasets
/// </summary>
public class GetAllDataSetsQuery : Query<List<DataSetSummary>>
{
}

/// <summary>
/// Summary information for a dataset
/// </summary>
public class DataSetSummary
{
    public Guid DatasetId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public double FileSizeMB { get; set; }
    public DateTime CreatedAt { get; set; }
}
