using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.ValueObjects;

public class ColumnProfile
{
    public string Name { get; set; } = string.Empty;
    public InferredType InferredType { get; set; }
    public double NullRate { get; set; }
    public int DistinctCount { get; set; }
    public List<string> TopValues { get; set; } = new();
    
    /// <summary>
    /// Minimum value (only for numeric columns)
    /// </summary>
    public double? Min { get; set; }
    
    /// <summary>
    /// Maximum value (only for numeric columns)
    /// </summary>
    public double? Max { get; set; }
}
