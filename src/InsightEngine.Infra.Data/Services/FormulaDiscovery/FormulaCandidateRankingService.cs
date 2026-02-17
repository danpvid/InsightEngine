using InsightEngine.Domain.Models.FormulaDiscovery;

namespace InsightEngine.Infra.Data.Services.FormulaDiscovery;

public sealed class FormulaCandidateRankingService
{
    private readonly LinearRegressionService _linearRegressionService;

    public FormulaCandidateRankingService(LinearRegressionService linearRegressionService)
    {
        _linearRegressionService = linearRegressionService;
    }

    public IReadOnlyList<FormulaCandidate> BuildCandidates(
        FormulaSampleSet sample,
        int maxCandidates = 3,
        bool enableInteractions = true,
        bool enableRatios = false,
        int maxExpandedTerms = 25,
        double minR2Improvement = 0.01d)
    {
        if (sample.X.Length == 0 || sample.Y.Length == 0 || sample.FeatureColumns.Count == 0)
        {
            return Array.Empty<FormulaCandidate>();
        }

        var boundedCandidates = Math.Clamp(maxCandidates, 1, 5);
        var candidates = new List<FormulaCandidate>();

        var baseFit = _linearRegressionService.FitRidge(sample.X, sample.Y);
        var baseCandidate = ToCandidate(
            sample,
            FormulaModelType.Linear,
            sample.FeatureColumns,
            baseFit,
            notes: new[]
            {
                "Best-fit linear equation inferred from sampled data.",
                "Association and fit quality do not guarantee the true business formula."
            });
        candidates.Add(baseCandidate);

        var expandedFeatureSeed = SelectExpansionFeatureIndexes(sample.X, sample.Y, maxSeedFeatures: 6);

        if (enableInteractions && expandedFeatureSeed.Count >= 2)
        {
            var interactionData = BuildInteractionExpandedData(sample, expandedFeatureSeed, maxExpandedTerms);
            if (interactionData.FeatureNames.Count > sample.FeatureColumns.Count)
            {
                var interactionFit = _linearRegressionService.FitRidge(interactionData.X, sample.Y);
                var interactionCandidate = ToCandidate(
                    sample,
                    FormulaModelType.LinearWithInteractions,
                    interactionData.FeatureNames,
                    interactionFit,
                    notes: new[]
                    {
                        "Includes pairwise interaction terms for top base features.",
                        "Higher complexity can overfit; prefer simpler model when quality is similar."
                    });

                if (ShouldIncludeCandidate(baseCandidate, interactionCandidate, minR2Improvement))
                {
                    candidates.Add(interactionCandidate);
                }
            }
        }

        if (enableRatios && expandedFeatureSeed.Count >= 2)
        {
            var ratioData = BuildRatioExpandedData(sample, expandedFeatureSeed, maxExpandedTerms);
            if (ratioData.FeatureNames.Count > sample.FeatureColumns.Count)
            {
                var ratioFit = _linearRegressionService.FitRidge(ratioData.X, sample.Y);
                var ratioCandidate = ToCandidate(
                    sample,
                    FormulaModelType.LinearWithRatios,
                    ratioData.FeatureNames,
                    ratioFit,
                    notes: new[]
                    {
                        "Includes safe ratio terms xi/(xj+eps) for selected base features.",
                        "Ratios are sensitive to denominator scale and noise."
                    });

                if (ShouldIncludeCandidate(baseCandidate, ratioCandidate, minR2Improvement))
                {
                    candidates.Add(ratioCandidate);
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Metrics.R2)
            .ThenBy(candidate => candidate.Metrics.MAE)
            .ThenBy(candidate => candidate.Terms.Count)
            .Take(boundedCandidates)
            .ToList();
    }

    private FormulaCandidate ToCandidate(
        FormulaSampleSet sample,
        FormulaModelType modelType,
        IReadOnlyList<string> featureNames,
        LinearRegressionResult fit,
        IEnumerable<string> notes)
    {
        var terms = featureNames
            .Select((name, index) => new Term
            {
                FeatureName = name,
                Coefficient = fit.Coefficients[index]
            })
            .ToList();

        var confidence = MapConfidence(fit.Metrics, sample.Y);
        var candidate = new FormulaCandidate
        {
            TargetColumn = sample.TargetColumn,
            Terms = terms,
            Intercept = fit.Intercept,
            Metrics = new Metrics
            {
                SampleSize = fit.Metrics.SampleSize,
                R2 = fit.Metrics.R2,
                MAE = fit.Metrics.Mae,
                RMSE = fit.Metrics.Rmse,
                ResidualP95Abs = fit.Metrics.ResidualP95Abs,
                ResidualMeanAbs = fit.Metrics.ResidualMeanAbs
            },
            ModelType = modelType,
            Confidence = confidence,
            PrettyFormula = BuildPrettyFormula(sample.TargetColumn, fit.Intercept, terms),
            Notes = notes.ToList()
        };

        if (confidence == FormulaConfidenceLevel.DeterministicLike)
        {
            candidate.Notes.Add("Deterministic-like threshold met by fit and residual stability checks.");
        }

        return candidate;
    }

    private static bool ShouldIncludeCandidate(
        FormulaCandidate baseline,
        FormulaCandidate candidate,
        double minR2Improvement)
    {
        if (candidate.Confidence == FormulaConfidenceLevel.DeterministicLike)
        {
            return true;
        }

        return candidate.Metrics.R2 >= (baseline.Metrics.R2 + Math.Max(0d, minR2Improvement));
    }

    private static FormulaConfidenceLevel MapConfidence(RegressionMetrics metrics, IReadOnlyList<double> targetValues)
    {
        var targetScale = TargetScale(targetValues);
        var residualP95Ratio = targetScale <= 1e-12d ? 1d : metrics.ResidualP95Abs / targetScale;
        var residualMeanRatio = targetScale <= 1e-12d ? 1d : metrics.ResidualMeanAbs / targetScale;

        if (metrics.R2 >= 0.995d && residualP95Ratio <= 0.01d && residualMeanRatio <= 0.005d)
        {
            return FormulaConfidenceLevel.DeterministicLike;
        }

        if (metrics.R2 >= 0.98d)
        {
            return FormulaConfidenceLevel.High;
        }

        if (metrics.R2 >= 0.90d)
        {
            return FormulaConfidenceLevel.Medium;
        }

        return FormulaConfidenceLevel.Low;
    }

    private static double TargetScale(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 1d;
        }

        var min = values.Min();
        var max = values.Max();
        var range = Math.Abs(max - min);
        if (range > 1e-12d)
        {
            return range;
        }

        var absP95 = Percentile(values.Select(Math.Abs).ToArray(), 0.95d);
        return Math.Max(absP95, 1d);
    }

    private static string BuildPrettyFormula(string target, double intercept, IReadOnlyCollection<Term> terms)
    {
        var interceptText = $"{RoundForDisplay(intercept):0.####}";
        var parts = new List<string> { interceptText };

        foreach (var term in terms)
        {
            var sign = term.Coefficient >= 0d ? "+" : "-";
            parts.Add($"{sign} {Math.Abs(RoundForDisplay(term.Coefficient)):0.####}*{term.FeatureName}");
        }

        return $"{target} ≈ {string.Join(" ", parts)}";
    }

    private static double RoundForDisplay(double value)
    {
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static List<int> SelectExpansionFeatureIndexes(double[][] x, double[] y, int maxSeedFeatures)
    {
        var featureCount = x[0].Length;
        var ranked = new List<(int Index, double Score)>();

        for (var col = 0; col < featureCount; col++)
        {
            var column = x.Select(row => row[col]).ToArray();
            var score = Math.Abs(Pearson(y, column));
            if (double.IsFinite(score))
            {
                ranked.Add((col, score));
            }
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .Take(Math.Clamp(maxSeedFeatures, 2, featureCount))
            .Select(item => item.Index)
            .ToList();
    }

    private static (double[][] X, List<string> FeatureNames) BuildInteractionExpandedData(
        FormulaSampleSet sample,
        IReadOnlyList<int> seedIndexes,
        int maxExpandedTerms)
    {
        var featureNames = new List<string>(sample.FeatureColumns);
        var data = sample.X
            .Select(row => row.ToList())
            .ToList();

        var cap = Math.Clamp(maxExpandedTerms, 1, 100);
        var added = 0;
        for (var i = 0; i < seedIndexes.Count && added < cap; i++)
        {
            for (var j = i + 1; j < seedIndexes.Count && added < cap; j++)
            {
                var left = seedIndexes[i];
                var right = seedIndexes[j];
                var featureName = $"{sample.FeatureColumns[left]}*{sample.FeatureColumns[right]}";

                featureNames.Add(featureName);
                for (var rowIndex = 0; rowIndex < data.Count; rowIndex++)
                {
                    data[rowIndex].Add(sample.X[rowIndex][left] * sample.X[rowIndex][right]);
                }

                added++;
            }
        }

        return (data.Select(row => row.ToArray()).ToArray(), featureNames);
    }

    private static (double[][] X, List<string> FeatureNames) BuildRatioExpandedData(
        FormulaSampleSet sample,
        IReadOnlyList<int> seedIndexes,
        int maxExpandedTerms)
    {
        var featureNames = new List<string>(sample.FeatureColumns);
        var data = sample.X
            .Select(row => row.ToList())
            .ToList();

        var stdDevByFeature = seedIndexes.ToDictionary(
            index => index,
            index => StdDev(sample.X.Select(row => row[index]).ToArray()));

        var cap = Math.Clamp(maxExpandedTerms, 1, 100);
        var added = 0;

        for (var i = 0; i < seedIndexes.Count && added < cap; i++)
        {
            for (var j = 0; j < seedIndexes.Count && added < cap; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var numerator = seedIndexes[i];
                var denominator = seedIndexes[j];
                var eps = 1e-9d * Math.Max(1d, stdDevByFeature[denominator]);
                var featureName = $"{sample.FeatureColumns[numerator]}/({sample.FeatureColumns[denominator]}+eps)";

                featureNames.Add(featureName);
                for (var rowIndex = 0; rowIndex < data.Count; rowIndex++)
                {
                    var denom = sample.X[rowIndex][denominator] + eps;
                    var ratio = Math.Abs(denom) <= 1e-18d
                        ? 0d
                        : sample.X[rowIndex][numerator] / denom;
                    data[rowIndex].Add(double.IsFinite(ratio) ? ratio : 0d);
                }

                added++;
            }
        }

        return (data.Select(row => row.ToArray()).ToArray(), featureNames);
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

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0d;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var clamped = Math.Clamp(percentile, 0d, 1d);
        var position = (ordered.Length - 1) * clamped;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = position - lowerIndex;
        return ordered[lowerIndex] + ((ordered[upperIndex] - ordered[lowerIndex]) * weight);
    }
}
