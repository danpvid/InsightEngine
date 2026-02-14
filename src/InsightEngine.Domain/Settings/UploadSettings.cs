namespace InsightEngine.Domain.Settings;

public class UploadSettings
{
    public string BasePath { get; set; } = "uploads";
    public long MaxFileSizeBytes { get; set; } = 20L * 1024 * 1024; // 20MB
    public int ProfileSampleSize { get; set; } = 5000;
}
