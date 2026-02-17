namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaSampleSet
{
    public required string TargetColumn { get; init; }
    public required List<string> FeatureColumns { get; init; }
    public required double[][] X { get; init; }
    public required double[] Y { get; init; }
    public required int OriginalRowCount { get; init; }
    public required int AcceptedRowCount { get; init; }
    public required int DroppedRowCount { get; init; }
}

public sealed class RegressionMetrics
{
    public int SampleSize { get; init; }
    public double R2 { get; init; }
    public double Mae { get; init; }
    public double Rmse { get; init; }
    public double ResidualP95Abs { get; init; }
    public double ResidualMeanAbs { get; init; }
}

public sealed class LinearRegressionResult
{
    public required double Intercept { get; init; }
    public required double[] Coefficients { get; init; }
    public required double[] Predictions { get; init; }
    public required double[] Residuals { get; init; }
    public required RegressionMetrics Metrics { get; init; }
    public required double RidgeLambdaUsed { get; init; }
}

public sealed class FeatureSelectionResult
{
    public required IReadOnlyList<string> CandidateFeatures { get; init; }
    public required IReadOnlyList<string> SelectedFeatures { get; init; }
    public required Dictionary<string, double> CorrelationByFeature { get; init; }
    public required IReadOnlyList<string> ExcludedFeatures { get; init; }
}
