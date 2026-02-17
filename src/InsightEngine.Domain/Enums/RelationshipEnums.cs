namespace InsightEngine.Domain.Enums;

public enum DriverType
{
    Numeric,
    Categorical,
    DateTime,
    Boolean
}

public enum AssociationMethod
{
    Pearson,
    Spearman,
    EtaSquared,
    CramersV,
    MutualInformation,
    SegmentDelta
}
