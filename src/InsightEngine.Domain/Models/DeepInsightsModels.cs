namespace InsightEngine.Domain.Models;

public class DeepInsightsRequest
{
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string Language { get; set; } = "pt-br";
    public string? Aggregation { get; set; }
    public string? TimeBin { get; set; }
    public string? MetricY { get; set; }
    public string? GroupBy { get; set; }
    public List<ChartFilter> Filters { get; set; } = new();
    public ScenarioRequest? Scenario { get; set; }
    public int? Horizon { get; set; }
    public bool SensitiveMode { get; set; }
    public string RequesterKey { get; set; } = "anonymous";
}

public class DeepInsightsResult
{
    public DeepInsightReport Report { get; set; } = new();
    public AiGenerationMeta Meta { get; set; } = new();
    public DeepInsightsExplainability Explainability { get; set; } = new();
    public EvidencePack? EvidencePack { get; set; }
}

public class DeepInsightsExplainability
{
    public int EvidenceUsedCount { get; set; }
    public List<string> TopEvidenceIdsUsed { get; set; } = new();
}

public class DeepInsightReport
{
    public string Headline { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public List<DeepInsightFinding> KeyFindings { get; set; } = new();
    public List<DeepInsightDriver> Drivers { get; set; } = new();
    public List<DeepInsightRisk> RisksAndCaveats { get; set; } = new();
    public DeepInsightProjectionSection Projections { get; set; } = new();
    public List<DeepInsightAction> RecommendedActions { get; set; } = new();
    public List<string> NextQuestions { get; set; } = new();
    public List<DeepInsightCitation> Citations { get; set; } = new();
    public DeepInsightMeta Meta { get; set; } = new();
}

public class DeepInsightFinding
{
    public string Title { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = new();
    public string Severity { get; set; } = "medium";
}

public class DeepInsightDriver
{
    public string Driver { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = new();
}

public class DeepInsightRisk
{
    public string Risk { get; set; } = string.Empty;
    public string Mitigation { get; set; } = string.Empty;
    public List<string> EvidenceIds { get; set; } = new();
}

public class DeepInsightProjectionSection
{
    public string Horizon { get; set; } = string.Empty;
    public List<DeepInsightProjectionMethod> Methods { get; set; } = new();
    public string Conclusion { get; set; } = string.Empty;
}

public class DeepInsightProjectionMethod
{
    public string Method { get; set; } = string.Empty;
    public string Narrative { get; set; } = string.Empty;
    public string Confidence { get; set; } = "medium";
    public List<string> EvidenceIds { get; set; } = new();
}

public class DeepInsightAction
{
    public string Action { get; set; } = string.Empty;
    public string ExpectedImpact { get; set; } = string.Empty;
    public string Effort { get; set; } = "medium";
    public List<string> EvidenceIds { get; set; } = new();
}

public class DeepInsightCitation
{
    public string EvidenceId { get; set; } = string.Empty;
    public string ShortClaim { get; set; } = string.Empty;
}

public class DeepInsightMeta
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string EvidenceVersion { get; set; } = string.Empty;
}

public class EvidencePackResult
{
    public EvidencePack EvidencePack { get; set; } = new();
    public bool CacheHit { get; set; }
}

public class EvidencePack
{
    public string EvidenceVersion { get; set; } = "v1";
    public Guid DatasetId { get; set; }
    public string RecommendationId { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public DatasetQualityEvidence DatasetQuality { get; set; } = new();
    public List<DistributionStatsEvidence> DistributionStats { get; set; } = new();
    public TimeSeriesStatsEvidence? TimeSeriesStats { get; set; }
    public List<SegmentBreakdownEvidence> SegmentBreakdowns { get; set; } = new();
    public ForecastPack ForecastPack { get; set; } = new();
    public WhatIfConclusionPack? WhatIfConclusionPack { get; set; }
    public List<EvidenceFact> Facts { get; set; } = new();
    public List<AggregatedSamplePoint> AggregatedSample { get; set; } = new();
    public int SerializedBytes { get; set; }
    public bool Truncated { get; set; }
}

public class DatasetQualityEvidence
{
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public double MissingRateAverage { get; set; }
    public double MissingRateMax { get; set; }
    public string MissingRateMaxColumn { get; set; } = string.Empty;
    public double DuplicateRate { get; set; }
    public string TimestampCoverage { get; set; } = "unknown";
    public int InvalidDateCount { get; set; }
    public int ImpossibleValueCount { get; set; }
    public bool ExtremeSkewDetected { get; set; }
}

public class DistributionStatsEvidence
{
    public string Metric { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double StdDev { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double P05 { get; set; }
    public double P25 { get; set; }
    public double P75 { get; set; }
    public double P95 { get; set; }
    public double Iqr { get; set; }
    public double CoefficientOfVariation { get; set; }
    public double SkewnessProxy { get; set; }
}

public class TimeSeriesStatsEvidence
{
    public int PointCount { get; set; }
    public double TrendSlope { get; set; }
    public string TrendClassification { get; set; } = "Flat";
    public double VolatilityRatio { get; set; }
    public double VolatilityBandLow { get; set; }
    public double VolatilityBandHigh { get; set; }
    public double WeeklyPatternStrength { get; set; }
    public double MonthlyPatternStrength { get; set; }
    public List<ChangePointEvidence> ChangePoints { get; set; } = new();
}

public class ChangePointEvidence
{
    public string Position { get; set; } = string.Empty;
    public double ShiftMagnitude { get; set; }
}

public class SegmentBreakdownEvidence
{
    public string Segment { get; set; } = string.Empty;
    public double ContributionValue { get; set; }
    public double SharePercent { get; set; }
    public double StabilityScore { get; set; }
    public bool IsOutlierSegment { get; set; }
}

public class ForecastPack
{
    public int Horizon { get; set; }
    public string Label { get; set; } = "baseline projection";
    public List<ForecastMethodEvidence> Methods { get; set; } = new();
}

public class ForecastMethodEvidence
{
    public string Method { get; set; } = string.Empty;
    public double ResidualStdDev { get; set; }
    public double Rmse { get; set; }
    public List<ForecastPointEvidence> Points { get; set; } = new();
}

public class ForecastPointEvidence
{
    public string Position { get; set; } = string.Empty;
    public double Value { get; set; }
    public double LowerBand { get; set; }
    public double UpperBand { get; set; }
}

public class WhatIfConclusionPack
{
    public double DeltaMean { get; set; }
    public double DeltaSum { get; set; }
    public double DeltaMax { get; set; }
    public double DeltaVolatility { get; set; }
    public double DeltaTrendSlope { get; set; }
    public List<string> TopDrivers { get; set; } = new();
}

public class AggregatedSamplePoint
{
    public string Series { get; set; } = string.Empty;
    public string X { get; set; } = string.Empty;
    public double Y { get; set; }
}

public class EvidenceFact
{
    public string EvidenceId { get; set; } = string.Empty;
    public string ShortClaim { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
