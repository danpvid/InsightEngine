using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models.MetadataIndex;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FeatureSelector
{
    private readonly FormulaSamplingService _samplingService;

    public FeatureSelector(FormulaSamplingService samplingService)
    {
        _samplingService = samplingService;
    }

    public async Task<FeatureSelectionResult> SelectTopFeaturesAsync(
        Guid datasetId,
        string targetColumn,
        IReadOnlyCollection<ColumnIndex> columns,
        long rowCount,
        int topK = 10,
        int sampleCap = 50_000,
        CancellationToken cancellationToken = default)
    {
        var boundedTopK = Math.Clamp(topK, 3, 20);
        var candidateFeatures = new List<string>();
        var excluded = new List<string>();

        foreach (var column in columns)
        {
            if (string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (column.InferredType != InferredType.Number)
            {
                excluded.Add($"{column.Name}: non-numeric inferred type");
                continue;
            }

            if (LooksLikeIdentifier(column, rowCount))
            {
                excluded.Add($"{column.Name}: looks like identifier/high-cardinality key");
                continue;
            }

            candidateFeatures.Add(column.Name);
        }

        var limitedCandidates = candidateFeatures
            .Take(60)
            .ToList();

        if (limitedCandidates.Count == 0)
        {
            return new FeatureSelectionResult
            {
                CandidateFeatures = Array.Empty<string>(),
                SelectedFeatures = Array.Empty<string>(),
                CorrelationByFeature = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                ExcludedFeatures = excluded
            };
        }

        var sample = await _samplingService.LoadSampleAsync(
            datasetId,
            targetColumn,
            limitedCandidates,
            sampleCap,
            cancellationToken);

        if (sample.AcceptedRowCount < 8 || sample.X.Length == 0 || sample.Y.Length == 0)
        {
            return new FeatureSelectionResult
            {
                CandidateFeatures = limitedCandidates,
                SelectedFeatures = limitedCandidates.Take(boundedTopK).ToList(),
                CorrelationByFeature = limitedCandidates.ToDictionary(
                    feature => feature,
                    _ => 0d,
                    StringComparer.OrdinalIgnoreCase),
                ExcludedFeatures = excluded
            };
        }

        var correlations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<(string Feature, double Score)>();

        for (var featureIndex = 0; featureIndex < sample.FeatureColumns.Count; featureIndex++)
        {
            var featureName = sample.FeatureColumns[featureIndex];
            var featureValues = sample.X.Select(row => row[featureIndex]).ToArray();

            var stdDev = StdDev(featureValues);
            if (stdDev <= 1e-9d)
            {
                excluded.Add($"{featureName}: near-constant values");
                correlations[featureName] = 0d;
                continue;
            }

            var corr = Pearson(sample.Y, featureValues);
            var absCorr = double.IsFinite(corr) ? Math.Abs(corr) : 0d;
            correlations[featureName] = absCorr;
            selected.Add((featureName, absCorr));
        }

        var ranked = selected
            .OrderByDescending(item => item.Score)
            .Take(boundedTopK)
            .Select(item => item.Feature)
            .ToList();

        return new FeatureSelectionResult
        {
            CandidateFeatures = limitedCandidates,
            SelectedFeatures = ranked,
            CorrelationByFeature = correlations,
            ExcludedFeatures = excluded
        };
    }

    private static bool LooksLikeIdentifier(ColumnIndex column, long rowCount)
    {
        if (rowCount <= 0)
        {
            return false;
        }

        var name = column.Name.ToLowerInvariant();
        var nameSuggestsKey = name.Contains("id", StringComparison.Ordinal)
            || name.Contains("key", StringComparison.Ordinal)
            || name.Contains("uuid", StringComparison.Ordinal)
            || name.Contains("guid", StringComparison.Ordinal)
            || name.Contains("codigo", StringComparison.Ordinal)
            || name.Contains("cod", StringComparison.Ordinal);

        var distinctRatio = Math.Clamp(column.DistinctCount / (double)rowCount, 0d, 1d);
        return nameSuggestsKey && distinctRatio >= 0.98d;
    }

    private static double Pearson(double[] x, double[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
        {
            return 0d;
        }

        var meanX = x.Average();
        var meanY = y.Average();

        double numerator = 0d;
        double denominatorX = 0d;
        double denominatorY = 0d;

        for (var i = 0; i < x.Length; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            numerator += dx * dy;
            denominatorX += dx * dx;
            denominatorY += dy * dy;
        }

        var denominator = Math.Sqrt(denominatorX * denominatorY);
        if (denominator <= 1e-12d)
        {
            return 0d;
        }

        return numerator / denominator;
    }

    private static double StdDev(double[] values)
    {
        if (values.Length <= 1)
        {
            return 0d;
        }

        var mean = values.Average();
        var variance = values
            .Select(value => (value - mean) * (value - mean))
            .Average();

        return Math.Sqrt(Math.Max(variance, 0d));
    }
}
