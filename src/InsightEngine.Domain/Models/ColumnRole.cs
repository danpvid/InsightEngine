using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Models;

public class ColumnRole
{
    public string ColumnName { get; set; } = string.Empty;
    public AxisRole Role { get; set; }
    public ColumnRoleHint RoleHint { get; set; }
    public InferredType InferredType { get; set; }
    public int DistinctCount { get; set; }
    public MeasureSemantic MeasureSemantic { get; set; } = MeasureSemantic.Generic;
    public bool IsIdLike { get; set; }
    public double MeasureScore { get; set; }
    public double DimensionScore { get; set; }
}
