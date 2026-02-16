namespace InsightEngine.API.Models;

public class AskDatasetRequest
{
    public string Question { get; set; } = string.Empty;
    public Dictionary<string, object?> CurrentView { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
