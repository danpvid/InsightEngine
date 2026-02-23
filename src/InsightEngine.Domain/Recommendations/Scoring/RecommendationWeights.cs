namespace InsightEngine.Domain.Recommendations.Scoring;

public class RecommendationWeights
{
    public const string SectionName = "RecommendationWeights";

    public double Correlation { get; set; } = 0.35;
    public double Variance { get; set; } = 0.20;
    public double Completeness { get; set; } = 0.15;
    public double Outlier { get; set; } = 0.05;
    public double Temporal { get; set; } = 0.10;
    public double RoleHint { get; set; } = 0.05;
    public double SemanticHint { get; set; } = 0.05;
    public double CardinalityPenalty { get; set; } = 0.03;
    public double NearConstantPenalty { get; set; } = 0.02;
}
