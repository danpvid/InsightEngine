namespace InsightEngine.Application.Insights.Models;

public class LlmTemporalSummary
{
    public string Column { get; set; } = string.Empty;
    public DateTime? MinDate { get; set; }
    public DateTime? MaxDate { get; set; }
    public string Granularity { get; set; } = "unknown";
}
