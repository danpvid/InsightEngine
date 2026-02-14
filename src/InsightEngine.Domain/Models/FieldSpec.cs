using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class FieldSpec
{
    public string Column { get; set; } = string.Empty;
    public AxisRole Role { get; set; }
    public Aggregation? Aggregation { get; set; }
    public TimeBin? Bin { get; set; }
}
