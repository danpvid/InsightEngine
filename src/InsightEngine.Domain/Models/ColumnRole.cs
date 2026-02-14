using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Models;

public class ColumnRole
{
    public string ColumnName { get; set; } = string.Empty;
    public AxisRole Role { get; set; }
    public InferredType InferredType { get; set; }
    public int DistinctCount { get; set; }
}
