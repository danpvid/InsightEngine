using System.Text.Json;
using InsightEngine.Domain.Models.Charts;

namespace InsightEngine.Domain.Models;

public class ChartRecommendation
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double Score { get; set; }
    public double ImpactScore { get; set; }
    public List<string> ScoreCriteria { get; set; } = new();
    public ChartMeta Chart { get; set; } = new();
    public ChartQuery Query { get; set; } = new();
    public Dictionary<string, object> OptionTemplate { get; set; } = new();
    public string TemplateType { get; set; } = "Standard";
    public RecommendationIncludedColumns IncludedColumns { get; set; } = new();
    public RecommendationAggregationPlan AggregationPlan { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    public AxisPolicy AxisPolicy { get; set; } = new();

    // Helper properties para acesso direto (Prompt 5)
    public string XColumn => Query.X.Column;
    public string YColumn => Query.Y.Column;
    public InsightEngine.Domain.Enums.Aggregation? Aggregation => Query.Y.Aggregation;
    public InsightEngine.Domain.Enums.TimeBin? TimeBin => Query.X.Bin;
}

public class RecommendationIncludedColumns
{
    public string? X { get; set; }
    public List<string> Y { get; set; } = new();
    public string? Series { get; set; }
    public List<string> Filters { get; set; } = new();
}

public class RecommendationAggregationPlan
{
    public string DefaultAggregation { get; set; } = "Avg";
    public List<string> SupportedAggregations { get; set; } = new();
}
