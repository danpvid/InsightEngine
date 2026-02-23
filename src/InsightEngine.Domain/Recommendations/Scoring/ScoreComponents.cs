namespace InsightEngine.Domain.Recommendations.Scoring;

public class ScoreComponents
{
    public double Correlation { get; set; }
    public double Variance { get; set; }
    public double Completeness { get; set; }
    public double Outlier { get; set; }
    public double Temporal { get; set; }
    public double RoleHint { get; set; }
    public double SemanticHint { get; set; }
    public double CardinalityPenalty { get; set; }
    public double NearConstantPenalty { get; set; }
    public double FinalScore { get; set; }
}
