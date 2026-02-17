namespace InsightEngine.Domain.Enums;

public enum CorrelationMethod
{
    Pearson,
    Spearman,
    CramersV,
    EtaSquared,
    MutualInformation
}

public enum CorrelationStrength
{
    Low,
    Medium,
    High
}

public enum CorrelationDirection
{
    None,
    Positive,
    Negative
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}
