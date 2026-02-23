namespace InsightEngine.CrossCutting.FeatureFlags;

public class InsightEngineFeatures
{
    public const string SectionName = "Features";

    public bool RecommendationV2Enabled { get; set; } = false;
    public bool RecommendationV2DebugLogging { get; set; } = false;
    public bool LlmStructuredInsightsV2Enabled { get; set; } = false;
    public bool ImportFinalizeWithIndexByDefault { get; set; } = false;
}
