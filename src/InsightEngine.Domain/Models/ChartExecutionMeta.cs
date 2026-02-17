namespace InsightEngine.Domain.Models;

/// <summary>
/// Metadata about chart execution performance and details
/// </summary>
public class ChartExecutionMeta
{
    /// <summary>
    /// Number of data points returned in the chart
    /// </summary>
    public int RowCountReturned { get; set; }
    
    /// <summary>
    /// Total execution time in milliseconds (full handler)
    /// </summary>
    public long ExecutionMs { get; set; }
    
    /// <summary>
    /// DuckDB query execution time in milliseconds
    /// </summary>
    public long DuckDbMs { get; set; }
    
    /// <summary>
    /// Type of chart executed (Line, Bar, Scatter, Histogram)
    /// </summary>
    public string ChartType { get; set; } = string.Empty;
    
    /// <summary>
    /// When the chart was generated (UTC)
    /// </summary>
    public DateTime GeneratedAt { get; set; }
    
    /// <summary>
    /// Optional: SHA256 hash of the query for future caching
    /// </summary>
    public string? QueryHash { get; set; }

    /// <summary>
    /// Indicates if response came from cache.
    /// </summary>
    public bool CacheHit { get; set; }

    /// <summary>
    /// Percentile drilldown metadata for current chart.
    /// </summary>
    public ChartPercentileMeta? Percentiles { get; set; }

    /// <summary>
    /// Active chart view metadata (base or percentile).
    /// </summary>
    public ChartViewMeta? View { get; set; }
}
