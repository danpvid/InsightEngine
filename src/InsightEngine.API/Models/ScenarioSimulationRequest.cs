using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.API.Models;

public class ScenarioSimulationRequest
{
    public string TargetMetric { get; set; } = string.Empty;
    public string TargetDimension { get; set; } = string.Empty;
    public Aggregation? Aggregation { get; set; }
    public bool PropagateTargetFormula { get; set; } = false;
    public List<ChartFilter> Filters { get; set; } = new();
    public List<ScenarioOperation> Operations { get; set; } = new();
}
