using InsightEngine.Domain.Enums;

using InsightEngine.Domain.Models.FormulaDiscovery;

namespace InsightEngine.Domain.Models.MetadataIndex;

public class DatasetIndex
{
    public Guid DatasetId { get; set; }
    public DateTime BuiltAtUtc { get; set; }
    public string Version { get; set; } = "1.0";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public DatasetQualityIndex Quality { get; set; } = new();
    public List<ColumnIndex> Columns { get; set; } = new();
    public List<KeyCandidate> CandidateKeys { get; set; } = new();
    public CorrelationIndex Correlations { get; set; } = new();
    public List<DatasetTag> Tags { get; set; } = new();
    public GlobalStatsIndex? Stats { get; set; }
    public IndexLimits Limits { get; set; } = new();
    public FormulaDiscoveryIndexEntry? FormulaDiscovery { get; set; }
}

public class FormulaDiscoveryIndexEntry
{
    public string CacheKey { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public FormulaDiscoveryResult Result { get; set; } = new();
}

public class ColumnIndex
{
    public string Name { get; set; } = string.Empty;
    public InferredType InferredType { get; set; }
    public double NullRate { get; set; }
    public long DistinctCount { get; set; }
    public NumericStatsIndex? NumericStats { get; set; }
    public DateStatsIndex? DateStats { get; set; }
    public StringStatsIndex? StringStats { get; set; }
    public List<string> TopValues { get; set; } = new();
    public List<string> SemanticTags { get; set; } = new();
}

public class NumericStatsIndex
{
    public double? Mean { get; set; }
    public double? StdDev { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? P5 { get; set; }
    public double? P10 { get; set; }
    public double? P50 { get; set; }
    public double? P90 { get; set; }
    public double? P95 { get; set; }
    public List<HistogramBinIndex> Histogram { get; set; } = new();
}

public class HistogramBinIndex
{
    public double LowerBound { get; set; }
    public double UpperBound { get; set; }
    public long Count { get; set; }
}

public class DateStatsIndex
{
    public DateTime? Min { get; set; }
    public DateTime? Max { get; set; }
    public List<DateDensityBinIndex> Coverage { get; set; } = new();
    public List<DateGapHintIndex> Gaps { get; set; } = new();
}

public class DateDensityBinIndex
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public long Count { get; set; }
}

public class DateGapHintIndex
{
    public DateTime GapStart { get; set; }
    public DateTime GapEnd { get; set; }
    public long ApproxMissingPeriods { get; set; }
}

public class StringStatsIndex
{
    public double AvgLength { get; set; }
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public List<string> PatternHints { get; set; } = new();
}

public class CorrelationIndex
{
    public int CandidateColumnCount { get; set; }
    public List<CorrelationEdge> Edges { get; set; } = new();
}

public class CorrelationEdge
{
    public string LeftColumn { get; set; } = string.Empty;
    public string RightColumn { get; set; } = string.Empty;
    public CorrelationMethod Method { get; set; }
    public double Score { get; set; }
    public CorrelationStrength Strength { get; set; }
    public CorrelationDirection Direction { get; set; }
    public long SampleSize { get; set; }
    public ConfidenceLevel Confidence { get; set; }
}

public class KeyCandidate
{
    public List<string> Columns { get; set; } = new();
    public double UniquenessRatio { get; set; }
    public double NullRate { get; set; }
    public ConfidenceLevel Confidence { get; set; }
}

public class DatasetQualityIndex
{
    public double DuplicateRowRate { get; set; }
    public MissingnessSummaryIndex MissingnessSummary { get; set; } = new();
    public int ParseIssuesCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class MissingnessSummaryIndex
{
    public long TotalMissingValues { get; set; }
    public double AverageNullRate { get; set; }
    public double MedianNullRate { get; set; }
    public int ColumnsWithNulls { get; set; }
}

public class DatasetTag
{
    public string Name { get; set; } = string.Empty;
    public string? Source { get; set; }
    public double Score { get; set; }
}

public class GlobalStatsIndex
{
    public long NumericColumnCount { get; set; }
    public long DateColumnCount { get; set; }
    public long CategoryColumnCount { get; set; }
    public long StringColumnCount { get; set; }
    public long BooleanColumnCount { get; set; }
}

public class IndexLimits
{
    public int MaxColumnsIndexed { get; set; }
    public int MaxColumnsForCorrelation { get; set; }
    public int TopKEdgesPerColumn { get; set; }
    public int SampleRows { get; set; }
    public bool IncludeStringPatterns { get; set; }
    public bool IncludeDistributions { get; set; }
}
