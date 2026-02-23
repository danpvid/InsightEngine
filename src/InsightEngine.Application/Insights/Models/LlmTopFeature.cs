namespace InsightEngine.Application.Insights.Models;

public class LlmTopFeature
{
    public string Name { get; set; } = string.Empty;
    public double CorrelationAbs { get; set; }
    public double NullRate { get; set; }
    public double VarianceNorm { get; set; }
    public double CardinalityRatio { get; set; }
    public List<string> RoleHints { get; set; } = new();
    public List<string> SemanticHints { get; set; } = new();
}
