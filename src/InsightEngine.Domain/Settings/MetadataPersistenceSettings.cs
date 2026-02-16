namespace InsightEngine.Domain.Settings;

public class MetadataPersistenceSettings
{
    public const string SectionName = "MetadataPersistence";

    public bool Enabled { get; set; } = true;
    public string ConnectionString { get; set; } = "Data Source=insightengine-metadata.db";
}
