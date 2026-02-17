using InsightEngine.Domain.Models.FormulaDiscovery;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDataSetFormulaDiscoveryQuery : Query<FormulaDiscoveryResult>
{
    public Guid DatasetId { get; set; }
    public string? Target { get; set; }
    public int MaxCandidates { get; set; } = 3;
    public int SampleCap { get; set; } = 50_000;
    public int TopKFeatures { get; set; } = 10;
    public bool EnableInteractions { get; set; } = true;
    public bool EnableRatios { get; set; } = false;
    public bool ForceRecompute { get; set; } = false;

    public FormulaDiscoveryOptions ToOptions()
    {
        return new FormulaDiscoveryOptions
        {
            MaxCandidates = MaxCandidates,
            SampleCap = SampleCap,
            TopKFeatures = TopKFeatures,
            EnableInteractions = EnableInteractions,
            EnableRatios = EnableRatios,
            ForceRecompute = ForceRecompute
        };
    }
}
