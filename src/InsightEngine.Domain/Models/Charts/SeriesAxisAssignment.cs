namespace InsightEngine.Domain.Models.Charts;

public class SeriesAxisAssignment
{
    public string SeriesName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string SemanticType { get; set; } = "Generic";
    public int YAxisIndex { get; set; }
    public int RecommendedAxisIndex { get; set; }
    public double ScaleRatioToPrimary { get; set; }
}
