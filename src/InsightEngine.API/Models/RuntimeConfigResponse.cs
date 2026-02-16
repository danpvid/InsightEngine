namespace InsightEngine.API.Models;

public class RuntimeConfigResponse
{
    public long UploadMaxBytes { get; set; }
    public double UploadMaxMb { get; set; }
    public int ScatterMaxPoints { get; set; }
    public int HistogramBinsMin { get; set; }
    public int HistogramBinsMax { get; set; }
    public int QueryResultMaxRows { get; set; }
    public int CacheTtlSeconds { get; set; }
    public int DefaultTimeoutSeconds { get; set; }
    public int RawTopValuesLimit { get; set; }
    public int RawTopRangesLimit { get; set; }
    public int RawRangeBinCount { get; set; }
}
