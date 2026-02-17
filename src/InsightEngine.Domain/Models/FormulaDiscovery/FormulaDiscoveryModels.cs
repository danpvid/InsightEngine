namespace InsightEngine.Domain.Models.FormulaDiscovery;

public enum FormulaModelType
{
    Linear,
    LinearWithInteractions,
    LinearWithRatios
}

public enum FormulaConfidenceLevel
{
    Low,
    Medium,
    High,
    DeterministicLike
}

public class FormulaCandidate
{
    public string TargetColumn { get; set; } = string.Empty;
    public List<Term> Terms { get; set; } = new();
    public double Intercept { get; set; }
    public Metrics Metrics { get; set; } = new();
    public FormulaModelType ModelType { get; set; }
    public FormulaConfidenceLevel Confidence { get; set; }
    public string PrettyFormula { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
}

public class Term
{
    public string FeatureName { get; set; } = string.Empty;
    public double Coefficient { get; set; }
}

public class Metrics
{
    public int SampleSize { get; set; }
    public double R2 { get; set; }
    public double MAE { get; set; }
    public double RMSE { get; set; }
    public double ResidualP95Abs { get; set; }
    public double ResidualMeanAbs { get; set; }
}

public class FormulaDiscoveryResult
{
    public Guid DatasetId { get; set; }
    public string TargetColumn { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public List<FormulaCandidate> Candidates { get; set; } = new();
    public List<string> ConsideredColumns { get; set; } = new();
    public List<string> ExcludedColumns { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
