namespace InsightEngine.Application.Insights.Models;

public class LlmFormulaSummary
{
    public string Expression { get; set; } = string.Empty;
    public double? Error { get; set; }
    public string ConfidenceBand { get; set; } = "unknown";
}
