using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.Analytics;

public class ColumnStatsLite
{
    public double NullRate { get; set; }
    public int DistinctCount { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? Stddev { get; set; }
    public double? P10 { get; set; }
    public double? P90 { get; set; }
    public double? P5 { get; set; }
    public double? P95 { get; set; }
    public double? OutlierRate { get; set; }
    public PercentageScaleHint? PercentageScaleHint { get; set; }
}
