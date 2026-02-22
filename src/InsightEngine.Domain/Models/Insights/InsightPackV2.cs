namespace InsightEngine.Domain.Models.Insights;

public class InsightPackV2
{
    public string Version { get; set; } = "2.0";
    public InsightPackComputationMeta Meta { get; set; } = new();
    public InsightDatasetSummary DatasetSummary { get; set; } = new();
    public InsightSchemaContext SchemaContext { get; set; } = new();
    public InsightDataQualityContext DataQuality { get; set; } = new();
    public InsightTargetStory? TargetStory { get; set; }
    public InsightRelationshipsContext Relationships { get; set; } = new();
    public List<InsightRecommendedAction> Actions { get; set; } = new();
    public List<InsightEvidenceAnchor> EvidenceIndex { get; set; } = new();
}

public class InsightDatasetSummary
{
    public Guid DatasetId { get; set; }
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public InsightTimeRange? TimeRange { get; set; }
    public string? CurrencyCode { get; set; }
    public InsightSamplingInfo SamplingInfo { get; set; } = new();
}

public class InsightTimeRange
{
    public string Min { get; set; } = string.Empty;
    public string Max { get; set; } = string.Empty;
}

public class InsightSamplingInfo
{
    public int SampleRowsUsed { get; set; }
    public string SampleStrategy { get; set; } = "profile-derived";
}

public class InsightSchemaContext
{
    public string? TargetColumn { get; set; }
    public List<string> IgnoredColumns { get; set; } = new();
    public List<InsightSchemaColumn> Columns { get; set; } = new();
}

public class InsightSchemaColumn
{
    public string Name { get; set; } = string.Empty;
    public string ConfirmedType { get; set; } = "String";
    public string SemanticType { get; set; } = "Generic";
    public string RoleHint { get; set; } = "Dimension";
    public string? PercentageScaleHint { get; set; }
    public List<string> PercentageDisplayExamples { get; set; } = new();
    public InsightFormattingHints FormattingHints { get; set; } = new();
}

public class InsightPackComputationMeta
{
    public long TotalMs { get; set; }
    public List<InsightQueryGroupTiming> QueryGroups { get; set; } = new();
    public InsightComputationLimits LimitsApplied { get; set; } = new();
}

public class InsightQueryGroupTiming
{
    public string Group { get; set; } = string.Empty;
    public long DuckDbMs { get; set; }
    public long ProcessingMs { get; set; }
}

public class InsightComputationLimits
{
    public int TopNumericColumns { get; set; } = 8;
    public int TopDimensions { get; set; } = 3;
}

public class InsightFormattingHints
{
    public int Decimals { get; set; }
    public string? CurrencySymbol { get; set; }
    public string? Unit { get; set; }
}

public class InsightDataQualityContext
{
    public List<InsightNullHotspot> NullHotspots { get; set; } = new();
    public double DuplicateRateEstimate { get; set; }
    public List<string> IdLikeCandidates { get; set; } = new();
    public List<InsightOutlierSummary> OutlierSummary { get; set; } = new();
    public List<InsightInconsistentFormat> InconsistentFormats { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class InsightNullHotspot
{
    public string Column { get; set; } = string.Empty;
    public double NullRate { get; set; }
}

public class InsightOutlierSummary
{
    public string Column { get; set; } = string.Empty;
    public double OutlierRate { get; set; }
    public double P5 { get; set; }
    public double P95 { get; set; }
    public double Iqr { get; set; }
}

public class InsightInconsistentFormat
{
    public string Column { get; set; } = string.Empty;
    public int ParseFailuresCount { get; set; }
}

public class InsightTargetStory
{
    public string TargetType { get; set; } = "Other";
    public string TargetOptimizationGoal { get; set; } = "Maximize";
    public InsightTargetTrend TargetTrend { get; set; } = new();
    public InsightTopSegmentsByTarget TopSegmentsByTarget { get; set; } = new();
    public InsightOffenderSummary Offenders { get; set; } = new();
    public InsightDriverCandidates DriverCandidates { get; set; } = new();
    public InsightTargetDecompositionHints TargetDecompositionHints { get; set; } = new();
}

public class InsightTargetTrend
{
    public string ByTimeBin { get; set; } = "month";
    public List<InsightTimeValuePoint> Series { get; set; } = new();
    public double VolatilityScore { get; set; }
    public List<InsightChangePoint> ChangePoints { get; set; } = new();
}

public class InsightTimeValuePoint
{
    public string Time { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class InsightChangePoint
{
    public string Time { get; set; } = string.Empty;
    public double Delta { get; set; }
}

public class InsightTopSegmentsByTarget
{
    public List<string> DimensionCandidates { get; set; } = new();
    public List<InsightSegmentImpact> Segments { get; set; } = new();
}

public class InsightSegmentImpact
{
    public string Dimension { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double AggregateValue { get; set; }
    public double Share { get; set; }
    public double DeltaVsPreviousPeriod { get; set; }
}

public class InsightOffenderSummary
{
    public List<InsightSegmentDelta> TopNegativeSegments { get; set; } = new();
    public List<InsightSegmentDelta> TopPositiveSegments { get; set; } = new();
}

public class InsightSegmentDelta
{
    public string Dimension { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Delta { get; set; }
    public double Magnitude { get; set; }
}

public class InsightDriverCandidates
{
    public List<InsightNumericDriver> NumericDrivers { get; set; } = new();
    public List<InsightCategoricalDriver> CategoricalDrivers { get; set; } = new();
}

public class InsightNumericDriver
{
    public string Feature { get; set; } = string.Empty;
    public double CorrelationPearson { get; set; }
    public double CorrelationSpearman { get; set; }
    public double MutualInfoApprox { get; set; }
    public string StrengthLabel { get; set; } = "Weak";
}

public class InsightCategoricalDriver
{
    public string Dimension { get; set; } = string.Empty;
    public double EffectSizeProxy { get; set; }
    public List<InsightSegmentImpact> TopCategoriesImpact { get; set; } = new();
}

public class InsightTargetDecompositionHints
{
    public List<string> MoneyPackMeasures { get; set; } = new();
    public List<InsightScaleRatioHint> ScaleRatios { get; set; } = new();
}

public class InsightScaleRatioHint
{
    public string Series { get; set; } = string.Empty;
    public double ScaleRatioToPrimary { get; set; }
    public int RecommendedAxisIndex { get; set; }
}

public class InsightRelationshipsContext
{
    public InsightCorrelationMatrixSummary CorrelationMatrixSummary { get; set; } = new();
    public List<InsightFunctionalDependencyHint> FunctionalDependenciesHints { get; set; } = new();
    public InsightTimeSeasonalityHints TimeSeasonalityHints { get; set; } = new();
}

public class InsightCorrelationMatrixSummary
{
    public List<InsightCorrelationPair> TopPositivePairs { get; set; } = new();
    public List<InsightCorrelationPair> TopNegativePairs { get; set; } = new();
    public List<InsightCorrelationPair> TopPairsInvolvingTarget { get; set; } = new();
}

public class InsightCorrelationPair
{
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
    public double Score { get; set; }
}

public class InsightFunctionalDependencyHint
{
    public string Determinant { get; set; } = string.Empty;
    public string Dependent { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public class InsightTimeSeasonalityHints
{
    public string WeekdayVsWeekendImpact { get; set; } = "NotAvailable";
    public string MonthOfYearEffect { get; set; } = "NotAvailable";
}

public class InsightRecommendedAction
{
    public string Title { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public List<string> EvidenceRefs { get; set; } = new();
    public string ExpectedImpactDirection { get; set; } = "IncreaseTarget";
}

public class InsightEvidenceAnchor
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}