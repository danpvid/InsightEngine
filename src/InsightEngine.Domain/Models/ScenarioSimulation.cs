using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models;

public class ScenarioRequest
{
    public string TargetMetric { get; set; } = string.Empty;
    public string TargetDimension { get; set; } = string.Empty;
    public Aggregation? Aggregation { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
    public List<ScenarioOperation> Operations { get; set; } = new();
}

public class ScenarioOperation
{
    public ScenarioOperationType Type { get; set; }
    public string? Column { get; set; }
    public List<string> Values { get; set; } = new();
    public double? Factor { get; set; }
    public double? Constant { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
}

public enum ScenarioOperationType
{
    RemoveCategory,
    MultiplyMetric,
    AddConstant,
    Clamp,
    FilterOut
}

public class ScenarioSimulationResponse
{
    public Guid DatasetId { get; set; }
    public string TargetMetric { get; set; } = string.Empty;
    public string TargetDimension { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public int RowCountReturned { get; set; }
    public long DuckDbMs { get; set; }
    public string GeneratedSql { get; set; } = string.Empty;
    public List<ScenarioSeriesPoint> BaselineSeries { get; set; } = new();
    public List<ScenarioSeriesPoint> SimulatedSeries { get; set; } = new();
    public List<ScenarioDeltaPoint> DeltaSeries { get; set; } = new();
    public ScenarioDeltaSummary DeltaSummary { get; set; } = new();
}

public class ScenarioSeriesPoint
{
    public string Dimension { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class ScenarioDeltaPoint
{
    public string Dimension { get; set; } = string.Empty;
    public double Baseline { get; set; }
    public double Simulated { get; set; }
    public double Delta { get; set; }
    public double? DeltaPercent { get; set; }
}

public class ScenarioDeltaSummary
{
    public double AverageDeltaPercent { get; set; }
    public double MaxDeltaPercent { get; set; }
    public double MinDeltaPercent { get; set; }
    public int ChangedPoints { get; set; }
}
