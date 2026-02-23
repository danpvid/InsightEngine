namespace InsightEngine.Application.Insights.Models;

public class LlmOutlierSummary
{
    public string Column { get; set; } = string.Empty;
    public double OutlierRate { get; set; }
}
