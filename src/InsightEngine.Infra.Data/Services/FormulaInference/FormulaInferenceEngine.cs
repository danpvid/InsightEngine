using System.Diagnostics;
using DuckDB.NET.Data;
using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models.Formulas;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services.FormulaInference;

public sealed class FormulaInferenceEngine : IFormulaInferenceEngine
{
    private readonly IDataSetRepository _dataSetRepository;
    private readonly IIndexStore _indexStore;
    private readonly IOptionsMonitor<FormulaInferenceSettings> _settingsMonitor;
    private readonly ILogger<FormulaInferenceEngine> _logger;

    public FormulaInferenceEngine(
        IDataSetRepository dataSetRepository,
        IIndexStore indexStore,
        IOptionsMonitor<FormulaInferenceSettings> settingsMonitor,
        ILogger<FormulaInferenceEngine> logger)
    {
        _dataSetRepository = dataSetRepository;
        _indexStore = indexStore;
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public async Task<FormulaInferenceResult> InferAsync(
        Guid datasetId,
        string targetColumn,
        IReadOnlyCollection<string>? numericColumnsCandidate = null,
        FormulaInferenceSettings? settingsOverride = null,
        CancellationToken cancellationToken = default)
    {
        var settings = NormalizeSettings(settingsOverride ?? _settingsMonitor.CurrentValue ?? new FormulaInferenceSettings());
        var warnings = new List<string>();
        var meta = new Dictionary<string, object?>();
        var stopwatch = Stopwatch.StartNew();

        var result = new FormulaInferenceResult
        {
            Status = FormulaInferenceStatus.Running,
            GeneratedAt = DateTimeOffset.UtcNow,
            TargetColumn = targetColumn,
            Candidates = new List<FormulaExpression>(),
            Warnings = Array.Empty<string>()
        };

        try
        {
            if (string.IsNullOrWhiteSpace(targetColumn))
            {
                result.Status = FormulaInferenceStatus.Failed;
                warnings.Add("targetColumn is required.");
                result.Warnings = warnings.ToArray();
                return result;
            }

            var dataSet = await _dataSetRepository.GetByIdAsync(datasetId);
            if (dataSet is null)
            {
                result.Status = FormulaInferenceStatus.Failed;
                warnings.Add("Dataset not found.");
                result.Warnings = warnings.ToArray();
                return result;
            }

            var index = await _indexStore.LoadAsync(datasetId, cancellationToken);
            if (index is null)
            {
                result.Status = FormulaInferenceStatus.Failed;
                warnings.Add("Dataset index not found.");
                result.Warnings = warnings.ToArray();
                return result;
            }

            var resolvedTarget = ResolveTarget(index, targetColumn);
            if (resolvedTarget is null)
            {
                result.Status = FormulaInferenceStatus.Failed;
                warnings.Add("Target column not found in index.");
                result.Warnings = warnings.ToArray();
                return result;
            }

            if (!resolvedTarget.InferredType.IsNumericLike())
            {
                result.Status = FormulaInferenceStatus.Failed;
                warnings.Add("Target column must be numeric.");
                result.Warnings = warnings.ToArray();
                return result;
            }

            result.TargetColumn = resolvedTarget.Name;

            var rankedColumns = SelectCandidateColumns(index, resolvedTarget.Name, numericColumnsCandidate, settings.MaxColumns);
            if (rankedColumns.Count == 0)
            {
                warnings.Add("No eligible numeric candidate columns were found.");
                result.Status = FormulaInferenceStatus.Completed;
                result.Warnings = warnings.ToArray();
                result.NumericCandidateColumns = Array.Empty<string>();
                result.Meta = new Dictionary<string, object?> { ["elapsedMs"] = stopwatch.ElapsedMilliseconds };
                return result;
            }

            result.NumericCandidateColumns = rankedColumns.Select(column => column.Name).ToArray();

            if (BudgetExceeded(stopwatch, settings.SearchBudgetMs))
            {
                warnings.Add("Search budget reached before sampling.");
                result.Status = FormulaInferenceStatus.Completed;
                result.Warnings = warnings.ToArray();
                result.Meta = new Dictionary<string, object?> { ["elapsedMs"] = stopwatch.ElapsedMilliseconds };
                return result;
            }

            var searchSample = LoadSample(dataSet, resolvedTarget.Name, rankedColumns, settings.InitialSampleRows, "search", cancellationToken);
            if (searchSample.RowCount == 0)
            {
                warnings.Add("No rows available in search sample after null filtering.");
                result.Status = FormulaInferenceStatus.Completed;
                result.Warnings = warnings.ToArray();
                result.Meta = new Dictionary<string, object?>
                {
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                    ["searchRows"] = 0
                };
                return result;
            }

            var validationSample = LoadSample(dataSet, resolvedTarget.Name, rankedColumns, settings.ValidationSampleRows, "validation", cancellationToken);
            var tinySample = searchSample.Take(Math.Min(30, searchSample.RowCount));

            var correlationByColumn = rankedColumns.ToDictionary(column => column.Name, column => column.Score, StringComparer.OrdinalIgnoreCase);
            var buildStats = new BuildStats();
            var generated = GenerateBeamCandidates(searchSample, tinySample, settings, stopwatch, buildStats, cancellationToken);

            var acceptedStrict = EvaluateCandidates(
                generated,
                searchSample,
                settings.EpsilonAbs,
                settings.DivisionZeroEpsilon,
                stopwatch,
                settings.SearchBudgetMs,
                cancellationToken);

            var accepted = acceptedStrict;
            var epsilonUsed = settings.EpsilonAbs;
            if (accepted.Count == 0 && settings.EpsilonAbsRelaxed > settings.EpsilonAbs)
            {
                warnings.Add("epsilon relaxed");
                epsilonUsed = settings.EpsilonAbsRelaxed;
                accepted = EvaluateCandidates(
                    generated,
                    searchSample,
                    settings.EpsilonAbsRelaxed,
                    settings.DivisionZeroEpsilon,
                    stopwatch,
                    settings.SearchBudgetMs,
                    cancellationToken);
            }

            var finalCandidates = new List<FormulaExpression>();
            foreach (var candidate in accepted)
            {
                if (BudgetExceeded(stopwatch, settings.SearchBudgetMs))
                {
                    warnings.Add("Search budget reached during validation.");
                    break;
                }

                var validation = EvaluateCandidate(
                    candidate.Node,
                    validationSample,
                    epsilonUsed,
                    settings.DivisionZeroEpsilon,
                    stopAtFirstError: true,
                    cancellationToken);

                if (!validation.Passed)
                {
                    continue;
                }

                var confidence = ResolveConfidence(validation.RowsTested, settings.ValidationSampleRows);
                if (confidence == FormulaConfidence.Low)
                {
                    continue;
                }

                finalCandidates.Add(new FormulaExpression
                {
                    ExpressionText = candidate.Node.ExpressionText,
                    TargetColumn = resolvedTarget.Name,
                    UsedColumns = candidate.Node.UsedColumns
                        .Select(index => rankedColumns[index].Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Depth = candidate.Node.Depth,
                    OperatorsUsed = candidate.Node.OperatorsUsed.ToArray(),
                    EpsilonMaxAbsError = epsilonUsed,
                    SampleRowsTested = validation.RowsTested,
                    RowsFailed = validation.RowsFailed,
                    Confidence = confidence,
                    Notes = "division by zero skipped rows: 0"
                });
            }

            var ordered = finalCandidates
                .OrderBy(expression => expression.UsedColumns.Length)
                .ThenBy(expression => expression.Depth)
                .ThenBy(expression => OperatorPenalty(expression.OperatorsUsed))
                .ThenByDescending(expression => expression.UsedColumns.Sum(column => correlationByColumn.TryGetValue(column, out var score) ? score : 0d))
                .Take(settings.MaxCandidatesReturned)
                .ToList();

            meta["elapsedMs"] = stopwatch.ElapsedMilliseconds;
            meta["searchRows"] = searchSample.RowCount;
            meta["validationRows"] = validationSample.RowCount;
            meta["generatedCandidates"] = buildStats.Generated;
            meta["deduplicatedCandidates"] = buildStats.Deduplicated;
            meta["invalidByDivision"] = buildStats.InvalidByDivision;
            meta["beamDepthReached"] = buildStats.MaxDepthReached;
            meta["epsilonUsed"] = epsilonUsed;

            if (ordered.Count == 0)
            {
                warnings.Add("No candidates satisfied max-error threshold on all tested rows.");
            }

            result.Status = FormulaInferenceStatus.Completed;
            result.Candidates = ordered;
            result.Warnings = warnings.ToArray();
            result.Meta = meta;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Formula inference failed for dataset {DatasetId} target {TargetColumn}", datasetId, targetColumn);
            result.Status = FormulaInferenceStatus.Failed;
            warnings.Add(ex.Message);
            result.Warnings = warnings.ToArray();
            result.Meta = new Dictionary<string, object?>
            {
                ["elapsedMs"] = stopwatch.ElapsedMilliseconds
            };
            return result;
        }
    }

    private static FormulaInferenceSettings NormalizeSettings(FormulaInferenceSettings input)
    {
        return new FormulaInferenceSettings
        {
            EnabledDefault = input.EnabledDefault,
            MaxColumns = Math.Clamp(input.MaxColumns, 2, 10),
            MaxDepth = Math.Clamp(input.MaxDepth, 2, 5),
            MaxCandidatesReturned = Math.Clamp(input.MaxCandidatesReturned, 1, 10),
            SearchBudgetMs = Math.Clamp(input.SearchBudgetMs, 300, 30_000),
            InitialSampleRows = Math.Clamp(input.InitialSampleRows, 50, 2_000),
            ValidationSampleRows = Math.Clamp(input.ValidationSampleRows, 200, 20_000),
            EpsilonAbs = Math.Max(input.EpsilonAbs, 1e-12),
            EpsilonAbsRelaxed = Math.Max(input.EpsilonAbsRelaxed, input.EpsilonAbs),
            DivisionZeroEpsilon = Math.Max(input.DivisionZeroEpsilon, 1e-15),
            AllowConstants = false,
            AllowColumnReuse = input.AllowColumnReuse,
            BeamWidth = Math.Clamp(input.BeamWidth, 20, 500)
        };
    }

    private static ColumnIndex? ResolveTarget(DatasetIndex index, string targetColumn)
    {
        return index.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase));
    }

    private static List<RankedColumn> SelectCandidateColumns(
        DatasetIndex index,
        string targetColumn,
        IReadOnlyCollection<string>? explicitCandidates,
        int maxColumns)
    {
        var allowed = explicitCandidates is null
            ? null
            : new HashSet<string>(explicitCandidates.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);

        var correlation = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in index.Correlations.Edges)
        {
            if (string.Equals(edge.LeftColumn, targetColumn, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(edge.RightColumn, targetColumn, StringComparison.OrdinalIgnoreCase))
            {
                correlation[edge.RightColumn] = Math.Max(Math.Abs(edge.Score), correlation.TryGetValue(edge.RightColumn, out var current) ? current : 0d);
                continue;
            }

            if (string.Equals(edge.RightColumn, targetColumn, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(edge.LeftColumn, targetColumn, StringComparison.OrdinalIgnoreCase))
            {
                correlation[edge.LeftColumn] = Math.Max(Math.Abs(edge.Score), correlation.TryGetValue(edge.LeftColumn, out var current) ? current : 0d);
            }
        }

        var ranked = index.Columns
            .Where(column => !string.Equals(column.Name, targetColumn, StringComparison.OrdinalIgnoreCase))
            .Where(column => column.InferredType.IsNumericLike())
            .Where(column => column.InferredType.NormalizeLegacy() != InferredType.Percentage)
            .Where(column => allowed is null || allowed.Contains(column.Name))
            .Select(column => new RankedColumn(
                column.Name,
                column.InferredType.NormalizeLegacy(),
                correlation.TryGetValue(column.Name, out var score) ? score : 0d))
            .OrderByDescending(column => column.Score)
            .ThenBy(column => NumericPriority(column.InferredType))
            .ThenBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxColumns)
            .ToList();

        return ranked;
    }

    private static int NumericPriority(InferredType inferredType)
    {
        return inferredType switch
        {
            InferredType.Money => 0,
            InferredType.Decimal => 1,
            InferredType.Integer => 2,
            _ => 3
        };
    }

    private static bool BudgetExceeded(Stopwatch stopwatch, int budgetMs)
    {
        return stopwatch.ElapsedMilliseconds >= budgetMs;
    }

    private static SampleSet LoadSample(
        DataSet dataSet,
        string targetColumn,
        IReadOnlyList<RankedColumn> columns,
        int rowLimit,
        string seed,
        CancellationToken cancellationToken)
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        var csvPath = EscapeSqlLiteral(dataSet.StoredPath);
        var source = $"read_csv_auto('{csvPath}', HEADER=true, SAMPLE_SIZE=-1, ALL_VARCHAR=true)";

        var projected = new List<string>
        {
            $"{BuildNumericExpression($"src.{EscapeIdentifier(targetColumn)}")} AS target"
        };

        projected.AddRange(columns.Select((column, index) =>
            $"{BuildNumericExpression($"src.{EscapeIdentifier(column.Name)}")} AS c{index}"));

        var notNullConditions = new List<string> { "target IS NOT NULL" };
        notNullConditions.AddRange(Enumerable.Range(0, columns.Count).Select(index => $"c{index} IS NOT NULL"));

        var hashFields = new List<string> { "COALESCE(CAST(target AS VARCHAR), '')" };
        hashFields.AddRange(Enumerable.Range(0, columns.Count).Select(index => $"COALESCE(CAST(c{index} AS VARCHAR), '')"));
        hashFields.Add($"'{EscapeSqlLiteral(seed)}'");
        var hashExpr = $"hash(concat_ws('|', {string.Join(", ", hashFields)}))";

        var sql = $@"
WITH source AS (
    SELECT {string.Join(", ", projected)}
    FROM {source} AS src
),
filtered AS (
    SELECT *
    FROM source
    WHERE {string.Join(" AND ", notNullConditions)}
),
sampled AS (
    SELECT *
    FROM filtered
    ORDER BY {hashExpr}
    LIMIT {rowLimit}
)
SELECT target, {string.Join(", ", Enumerable.Range(0, columns.Count).Select(index => $"c{index}"))}
FROM sampled;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var targets = new List<double>();
        var rows = new List<double[]>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Convert.ToDouble(reader.GetValue(0));
            if (!double.IsFinite(target))
            {
                continue;
            }

            var values = new double[columns.Count];
            var validRow = true;
            for (var col = 0; col < columns.Count; col++)
            {
                var value = Convert.ToDouble(reader.GetValue(col + 1));
                if (!double.IsFinite(value))
                {
                    validRow = false;
                    break;
                }

                values[col] = value;
            }

            if (!validRow)
            {
                continue;
            }

            targets.Add(target);
            rows.Add(values);
        }

        return new SampleSet(rows.ToArray(), targets.ToArray(), columns.Select(column => column.Name).ToArray());
    }

    private static List<ScoredNode> GenerateBeamCandidates(
        SampleSet searchSample,
        SampleSet tinySample,
        FormulaInferenceSettings settings,
        Stopwatch stopwatch,
        BuildStats stats,
        CancellationToken cancellationToken)
    {
        var leaves = new List<ExpressionNode>();
        for (var index = 0; index < searchSample.ColumnNames.Length; index++)
        {
            var columnName = searchSample.ColumnNames[index];
            var signature = $"col({columnName.ToLowerInvariant()})";
            leaves.Add(ExpressionNode.Leaf(index, columnName, signature));
        }

        var frontier = new List<ExpressionNode>(leaves);
        var acceptedBySignature = new Dictionary<string, ScoredNode>(StringComparer.Ordinal);
        var seenByDepth = new HashSet<string>(StringComparer.Ordinal);

        for (var depth = 2; depth <= settings.MaxDepth; depth++)
        {
            if (BudgetExceeded(stopwatch, settings.SearchBudgetMs))
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var generated = new List<ScoredNode>();
            var pool = frontier.Concat(leaves).DistinctBy(node => node.Signature).ToList();

            for (var leftIndex = 0; leftIndex < pool.Count; leftIndex++)
            {
                var left = pool[leftIndex];
                for (var rightIndex = 0; rightIndex < pool.Count; rightIndex++)
                {
                    var right = pool[rightIndex];
                    if (!settings.AllowColumnReuse && left.UsedColumns.Overlaps(right.UsedColumns))
                    {
                        continue;
                    }

                    foreach (var op in Enum.GetValues<FormulaOperator>())
                    {
                        if ((op == FormulaOperator.Add || op == FormulaOperator.Multiply) && rightIndex < leftIndex)
                        {
                            continue;
                        }

                        if ((op == FormulaOperator.Subtract || op == FormulaOperator.Divide)
                            && left.Signature == right.Signature)
                        {
                            continue;
                        }

                        var node = ExpressionNode.Combine(left, right, op);
                        if (node.Depth != depth)
                        {
                            continue;
                        }

                        stats.Generated++;
                        if (!seenByDepth.Add(node.Signature))
                        {
                            stats.Deduplicated++;
                            continue;
                        }

                        var heuristic = ScoreNodeByCorrelation(node, tinySample, settings.DivisionZeroEpsilon, out var invalidByDivision);
                        if (invalidByDivision)
                        {
                            stats.InvalidByDivision++;
                            continue;
                        }

                        if (heuristic <= 0)
                        {
                            continue;
                        }

                        generated.Add(new ScoredNode(node, heuristic));
                    }

                    if (BudgetExceeded(stopwatch, settings.SearchBudgetMs))
                    {
                        break;
                    }
                }

                if (BudgetExceeded(stopwatch, settings.SearchBudgetMs))
                {
                    break;
                }
            }

            if (generated.Count == 0)
            {
                break;
            }

            frontier = generated
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Node.Depth)
                .Take(settings.BeamWidth)
                .Select(item => item.Node)
                .ToList();

            stats.MaxDepthReached = depth;
            foreach (var item in generated)
            {
                acceptedBySignature[item.Node.Signature] = item;
            }
        }

        foreach (var leaf in leaves)
        {
            acceptedBySignature.TryAdd(leaf.Signature, new ScoredNode(leaf, 0.001));
        }

        return acceptedBySignature.Values
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Node.Depth)
            .Take(settings.BeamWidth * settings.MaxDepth)
            .ToList();
    }

    private static double ScoreNodeByCorrelation(ExpressionNode node, SampleSet sample, double divisionZeroEpsilon, out bool invalidByDivision)
    {
        invalidByDivision = false;
        if (sample.RowCount == 0)
        {
            return 0d;
        }

        var values = new double[sample.RowCount];
        for (var row = 0; row < sample.RowCount; row++)
        {
            if (!TryEvaluate(node, sample.Values[row], divisionZeroEpsilon, out var value))
            {
                invalidByDivision = true;
                return 0d;
            }

            if (!double.IsFinite(value))
            {
                return 0d;
            }

            values[row] = value;
        }

        var corr = Math.Abs(Pearson(values, sample.Target));
        return double.IsFinite(corr) ? corr : 0d;
    }

    private static List<ScoredNode> EvaluateCandidates(
        IReadOnlyCollection<ScoredNode> generated,
        SampleSet sample,
        double epsilon,
        double divisionZeroEpsilon,
        Stopwatch stopwatch,
        int budgetMs,
        CancellationToken cancellationToken)
    {
        var accepted = new List<ScoredNode>();
        foreach (var candidate in generated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BudgetExceeded(stopwatch, budgetMs))
            {
                break;
            }

            var evaluation = EvaluateCandidate(candidate.Node, sample, epsilon, divisionZeroEpsilon, stopAtFirstError: true, cancellationToken);
            if (!evaluation.Passed)
            {
                continue;
            }

            accepted.Add(candidate);
        }

        return accepted;
    }

    private static CandidateEvaluation EvaluateCandidate(
        ExpressionNode node,
        SampleSet sample,
        double epsilon,
        double divisionZeroEpsilon,
        bool stopAtFirstError,
        CancellationToken cancellationToken)
    {
        var tested = 0;
        var failed = 0;

        for (var row = 0; row < sample.RowCount; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tested++;

            if (!TryEvaluate(node, sample.Values[row], divisionZeroEpsilon, out var value))
            {
                failed++;
                return new CandidateEvaluation(false, tested, failed);
            }

            var absError = Math.Abs(value - sample.Target[row]);
            if (absError > epsilon)
            {
                failed++;
                if (stopAtFirstError)
                {
                    return new CandidateEvaluation(false, tested, failed);
                }
            }
        }

        return new CandidateEvaluation(failed == 0, tested, failed);
    }

    private static bool TryEvaluate(ExpressionNode node, IReadOnlyList<double> row, double divisionZeroEpsilon, out double value)
    {
        if (node.LeafIndex.HasValue)
        {
            value = row[node.LeafIndex.Value];
            return true;
        }

        value = 0;
        if (node.Left is null || node.Right is null || !node.Operator.HasValue)
        {
            return false;
        }

        if (!TryEvaluate(node.Left, row, divisionZeroEpsilon, out var left))
        {
            return false;
        }

        if (!TryEvaluate(node.Right, row, divisionZeroEpsilon, out var right))
        {
            return false;
        }

        value = node.Operator.Value switch
        {
            FormulaOperator.Add => left + right,
            FormulaOperator.Subtract => left - right,
            FormulaOperator.Multiply => left * right,
            FormulaOperator.Divide => Math.Abs(right) < divisionZeroEpsilon ? double.NaN : left / right,
            _ => double.NaN
        };

        return double.IsFinite(value);
    }

    private static FormulaConfidence ResolveConfidence(int testedRows, int validationSampleRows)
    {
        if (testedRows >= validationSampleRows)
        {
            return FormulaConfidence.High;
        }

        if (testedRows >= 500)
        {
            return FormulaConfidence.Medium;
        }

        return FormulaConfidence.Low;
    }

    private static int OperatorPenalty(IReadOnlyCollection<FormulaOperator> operators)
    {
        var score = 0;
        foreach (var op in operators)
        {
            score += op switch
            {
                FormulaOperator.Add => 0,
                FormulaOperator.Subtract => 0,
                FormulaOperator.Multiply => 1,
                FormulaOperator.Divide => 3,
                _ => 2
            };
        }

        return score;
    }

    private static double Pearson(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        var meanLeft = left.Average();
        var meanRight = right.Average();

        double numerator = 0;
        double denominatorLeft = 0;
        double denominatorRight = 0;

        for (var i = 0; i < left.Count; i++)
        {
            var dl = left[i] - meanLeft;
            var dr = right[i] - meanRight;
            numerator += dl * dr;
            denominatorLeft += dl * dl;
            denominatorRight += dr * dr;
        }

        var denominator = Math.Sqrt(denominatorLeft * denominatorRight);
        if (denominator < 1e-12)
        {
            return 0;
        }

        return numerator / denominator;
    }

    private static string BuildNumericExpression(string sourceExpression)
    {
        return $"TRY_CAST(REPLACE(CAST({sourceExpression} AS VARCHAR), ',', '') AS DOUBLE)";
    }

    private static string EscapeIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private sealed record RankedColumn(string Name, InferredType InferredType, double Score);

    private sealed record SampleSet(double[][] Values, double[] Target, string[] ColumnNames)
    {
        public int RowCount => Target.Length;

        public SampleSet Take(int count)
        {
            if (count >= RowCount)
            {
                return this;
            }

            return new SampleSet(Values.Take(count).ToArray(), Target.Take(count).ToArray(), ColumnNames);
        }
    }

    private sealed record CandidateEvaluation(bool Passed, int RowsTested, int RowsFailed);

    private sealed record ScoredNode(ExpressionNode Node, double Score);

    private sealed class BuildStats
    {
        public int Generated { get; set; }
        public int Deduplicated { get; set; }
        public int InvalidByDivision { get; set; }
        public int MaxDepthReached { get; set; }
    }

    private sealed class ExpressionNode
    {
        public int? LeafIndex { get; private init; }
        public ExpressionNode? Left { get; private init; }
        public ExpressionNode? Right { get; private init; }
        public FormulaOperator? Operator { get; private init; }
        public string ExpressionText { get; private init; } = string.Empty;
        public string Signature { get; private init; } = string.Empty;
        public int Depth { get; private init; }
        public HashSet<int> UsedColumns { get; private init; } = new();
        public IReadOnlyList<FormulaOperator> OperatorsUsed { get; private init; } = Array.Empty<FormulaOperator>();

        public static ExpressionNode Leaf(int index, string columnName, string signature)
        {
            return new ExpressionNode
            {
                LeafIndex = index,
                ExpressionText = EscapeIdentifier(columnName),
                Signature = signature,
                Depth = 1,
                UsedColumns = new HashSet<int> { index },
                OperatorsUsed = Array.Empty<FormulaOperator>()
            };
        }

        public static ExpressionNode Combine(ExpressionNode left, ExpressionNode right, FormulaOperator op)
        {
            var depth = Math.Max(left.Depth, right.Depth) + 1;
            var expression = $"({left.ExpressionText} {OperatorToken(op)} {right.ExpressionText})";
            var signature = BuildSignature(left.Signature, right.Signature, op);
            var used = new HashSet<int>(left.UsedColumns);
            used.UnionWith(right.UsedColumns);

            var operators = new List<FormulaOperator>(left.OperatorsUsed.Count + right.OperatorsUsed.Count + 1);
            operators.AddRange(left.OperatorsUsed);
            operators.AddRange(right.OperatorsUsed);
            operators.Add(op);

            return new ExpressionNode
            {
                Left = left,
                Right = right,
                Operator = op,
                ExpressionText = expression,
                Signature = signature,
                Depth = depth,
                UsedColumns = used,
                OperatorsUsed = operators
            };
        }

        private static string BuildSignature(string leftSignature, string rightSignature, FormulaOperator op)
        {
            if (op is FormulaOperator.Add or FormulaOperator.Multiply)
            {
                if (string.CompareOrdinal(leftSignature, rightSignature) > 0)
                {
                    (leftSignature, rightSignature) = (rightSignature, leftSignature);
                }
            }

            return $"{op}({leftSignature},{rightSignature})";
        }

        private static string OperatorToken(FormulaOperator op)
        {
            return op switch
            {
                FormulaOperator.Add => "+",
                FormulaOperator.Subtract => "-",
                FormulaOperator.Multiply => "*",
                FormulaOperator.Divide => "/",
                _ => "+"
            };
        }
    }
}
