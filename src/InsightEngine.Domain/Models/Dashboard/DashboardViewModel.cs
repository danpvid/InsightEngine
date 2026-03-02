using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Models.Dashboard;

public class DashboardViewModel
{
    public DashboardDatasetSummary? Dataset { get; set; }
    public List<DashboardKpiCard> Kpis { get; set; } = new();
    public List<ChartRecommendation> Charts { get; set; } = new();
    public DashboardTables Tables { get; set; } = new();
    public DashboardInsights Insights { get; set; } = new();
    public DashboardMetadata Metadata { get; set; } = new();
    public DateTime? LastUpdated { get; set; }
    public DashboardGenerationTimestamps Generation { get; set; } = new();
}

public class DashboardDatasetSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? TargetColumn { get; set; }
}

public class DashboardKpiCard
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Trend { get; set; }
}

public class DashboardTables
{
    public List<DashboardTopFeatureRow> TopFeatures { get; set; } = new();
    public List<DashboardDataQualityRow> DataQuality { get; set; } = new();
}

public class DashboardTopFeatureRow
{
    public string Column { get; set; } = string.Empty;
    public double Score { get; set; }
    public double? Correlation { get; set; }
    public double? VarianceNorm { get; set; }
    public double NullRate { get; set; }
    public double CardinalityRatio { get; set; }
}

public class DashboardDataQualityRow
{
    public string Column { get; set; } = string.Empty;
    public double NullRate { get; set; }
    public double OutlierRate { get; set; }
    public long DistinctCount { get; set; }
}

public class DashboardInsights
{
    public string? LlmExecutiveSummary { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class DashboardMetadata
{
    public bool IndexAvailable { get; set; }
    public bool RecommendationsAvailable { get; set; }
    public bool FormulaAvailable { get; set; }
    public DashboardFormulaSummary? FormulaSummary { get; set; }
}

public class DashboardFormulaSummary
{
    public string Expression { get; set; } = string.Empty;
    public double Error { get; set; }
    public string Confidence { get; set; } = string.Empty;
}

public class DashboardGenerationTimestamps
{
    public DateTime? IndexGeneratedAt { get; set; }
    public DateTime? RecommendationsGeneratedAt { get; set; }
    public DateTime? InsightsGeneratedAt { get; set; }
}
