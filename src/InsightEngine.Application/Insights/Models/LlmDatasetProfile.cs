namespace InsightEngine.Application.Insights.Models;

public class LlmDatasetProfile
{
    public string DatasetName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string? TargetColumn { get; set; }
    public Dictionary<string, int> TypesSummary { get; set; } = new();
    public List<LlmTopFeature> TopCorrelatedFeatures { get; set; } = new();
    public List<LlmTopFeature> HighVarianceFeatures { get; set; } = new();
    public List<LlmTopFeature> HighNullRateFeatures { get; set; } = new();
    public List<LlmOutlierSummary> OutlierColumns { get; set; } = new();
    public List<object> DominantCategories { get; set; } = new();
    public List<LlmTemporalSummary> TemporalPatterns { get; set; } = new();
    public List<string> RecommendedChartsSummary { get; set; } = new();
    public LlmFormulaSummary? FormulaInferenceSummary { get; set; }
}
