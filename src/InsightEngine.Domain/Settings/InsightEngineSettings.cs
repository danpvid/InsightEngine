namespace InsightEngine.Domain.Settings;

/// <summary>
/// Central runtime limits and defaults shared across API and UI.
/// </summary>
public class InsightEngineSettings
{
    public const string SectionName = "InsightEngineSettings";

    public long UploadMaxBytes { get; set; } = 20L * 1024 * 1024;
    public int ScatterMaxPoints { get; set; } = 2000;
    public int HistogramBinsMin { get; set; } = 5;
    public int HistogramBinsMax { get; set; } = 50;
    public int QueryResultMaxRows { get; set; } = 1000;
    public int CacheTtlSeconds { get; set; } = 300;
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int RetentionDays { get; set; } = 30;
    public int CleanupIntervalMinutes { get; set; } = 60;
}
