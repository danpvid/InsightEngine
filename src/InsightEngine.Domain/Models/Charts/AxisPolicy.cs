namespace InsightEngine.Domain.Models.Charts;

public class AxisPolicy
{
    public string DefaultMode { get; set; } = "SingleAxisBySemanticType";
    public int MaxAxes { get; set; } = 2;
    public double SuggestSeparateAxesWhenScaleRatioAbove { get; set; } = 50d;
    public bool AllowPerSeriesAxisOverride { get; set; } = true;
}
