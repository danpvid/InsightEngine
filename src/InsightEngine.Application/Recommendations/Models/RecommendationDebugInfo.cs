using InsightEngine.Domain.Recommendations.Scoring;

namespace InsightEngine.Application.Recommendations.Models;

public class RecommendationDebugInfo
{
    public string RecommendationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ScoreComponents Components { get; set; } = new();
}
