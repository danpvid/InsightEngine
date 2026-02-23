using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Queries.DataSet;

public class AskDatasetAnalysisPlanQuery : Query<AskAnalysisPlanResult>
{
    public Guid DatasetId { get; set; }
    public string Language { get; set; } = "pt-br";
    public string Question { get; set; } = string.Empty;
    public Dictionary<string, object?> CurrentView { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
