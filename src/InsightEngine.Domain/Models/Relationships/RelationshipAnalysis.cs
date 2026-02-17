using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.Relationships;

public class RelationshipAnalysis
{
    public Guid DatasetId { get; set; }
    public string TargetColumn { get; set; } = string.Empty;
    public DriverType TargetType { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public List<DriverCandidate> Drivers { get; set; } = new();
}

public class DriverCandidate
{
    public string Column { get; set; } = string.Empty;
    public DriverType DriverType { get; set; }
    public AssociationMethod Method { get; set; }
    public double Score { get; set; }
    public RelationshipEvidence Evidence { get; set; } = new();
}

public class RelationshipEvidence
{
    public double Score { get; set; }
    public CorrelationDirection Direction { get; set; } = CorrelationDirection.None;
    public long SampleSize { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<RelationshipTopSegment> TopSegments { get; set; } = new();
}

public class RelationshipTopSegment
{
    public string Segment { get; set; } = string.Empty;
    public long Count { get; set; }
    public double? Delta { get; set; }
    public double? Mean { get; set; }
}
