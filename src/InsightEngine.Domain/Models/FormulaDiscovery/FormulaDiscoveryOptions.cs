namespace InsightEngine.Domain.Models.FormulaDiscovery;

public class FormulaDiscoveryOptions
{
    public int MaxCandidates { get; set; } = 3;
    public int SampleCap { get; set; } = 50_000;
    public int TopKFeatures { get; set; } = 10;
    public bool EnableInteractions { get; set; } = true;
    public bool EnableRatios { get; set; } = false;
    public bool ForceRecompute { get; set; } = false;
}
