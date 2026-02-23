using System.Text;
using System.Text.Json;
using InsightEngine.Application.Insights.Models;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.Recommendations.Scoring;

namespace InsightEngine.Application.Insights;

public class LlmInsightComposerV2
{
    private const int MaxPayloadBytes = 25 * 1024;

    public (string SystemPrompt, string UserPrompt, string PayloadJson) Compose(
        DatasetIndex index,
        IReadOnlyList<ChartRecommendation> recommendations,
        string language,
        IEnumerable<string>? ignoredColumns = null)
    {
        var ignored = new HashSet<string>(ignoredColumns ?? [], StringComparer.OrdinalIgnoreCase);
        var profile = BuildProfile(index, recommendations, ignored);
        var payloadJson = SerializeWithSizeGuard(profile);

        var systemPrompt = BuildSystemPrompt(language);
        var userPrompt = BuildUserPrompt(language, payloadJson);

        return (systemPrompt, userPrompt, payloadJson);
    }

    private static LlmDatasetProfile BuildProfile(
        DatasetIndex index,
        IReadOnlyList<ChartRecommendation> recommendations,
        HashSet<string> ignored)
    {
        var activeColumns = index.Columns
            .Where(column => !ignored.Contains(column.Name))
            .ToList();

        var typeSummary = activeColumns
            .GroupBy(column => column.InferredType.ToString())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var topCorrelated = ResolveTopCorrelated(index, activeColumns, 8);
        var highVariance = ResolveHighVariance(activeColumns, index.RowCount, 8);
        var highNull = activeColumns
            .OrderByDescending(column => column.NullRate)
            .Take(5)
            .Select(column => MapFeature(column, index.RowCount))
            .ToList();

        var outlierColumns = activeColumns
            .Select(column => new LlmOutlierSummary
            {
                Column = column.Name,
                OutlierRate = EstimateOutlierRate(column)
            })
            .OrderByDescending(item => item.OutlierRate)
            .Take(5)
            .ToList();

        var temporal = activeColumns
            .Where(column => column.DateStats is not null || column.InferredType == InferredType.Date)
            .Take(5)
            .Select(column => new LlmTemporalSummary
            {
                Column = column.Name,
                MinDate = column.DateStats?.Min,
                MaxDate = column.DateStats?.Max,
                Granularity = ResolveGranularity(column)
            })
            .ToList();

        var categories = activeColumns
            .Where(column => column.InferredType is InferredType.Category or InferredType.String)
            .Take(5)
            .Select(column => new
            {
                column = column.Name,
                topValues = column.TopValues.Take(3).ToList()
            })
            .Cast<object>()
            .ToList();

        var formula = index.FormulaInference?.Result;
        var bestCandidate = formula?.Candidates.FirstOrDefault();
        var formulaSummary = formula is null || bestCandidate is null
            ? null
            : new LlmFormulaSummary
            {
                Expression = bestCandidate.ExpressionText,
                Error = bestCandidate.EpsilonMaxAbsError,
                ConfidenceBand = bestCandidate.Confidence.ToString()
            };

        return new LlmDatasetProfile
        {
            DatasetName = index.DatasetId.ToString(),
            RowCount = index.RowCount,
            ColumnCount = index.ColumnCount,
            TargetColumn = index.TargetColumn,
            TypesSummary = typeSummary,
            TopCorrelatedFeatures = topCorrelated,
            HighVarianceFeatures = highVariance,
            HighNullRateFeatures = highNull,
            OutlierColumns = outlierColumns,
            DominantCategories = categories,
            TemporalPatterns = temporal,
            RecommendedChartsSummary = recommendations.Take(12).Select(item => $"{item.Title}: {item.Reason}").ToList(),
            FormulaInferenceSummary = formulaSummary
        };
    }

    private static List<LlmTopFeature> ResolveTopCorrelated(
        DatasetIndex index,
        IReadOnlyCollection<ColumnIndex> columns,
        int take)
    {
        if (string.IsNullOrWhiteSpace(index.TargetColumn))
        {
            return [];
        }

        var edges = index.Correlations.Edges
            .Where(edge =>
                string.Equals(edge.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase)
                || string.Equals(edge.RightColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(edge => Math.Abs(edge.Score))
            .Take(take * 3)
            .ToList();

        var output = new List<LlmTopFeature>();
        foreach (var edge in edges)
        {
            var feature = string.Equals(edge.LeftColumn, index.TargetColumn, StringComparison.OrdinalIgnoreCase)
                ? edge.RightColumn
                : edge.LeftColumn;

            var column = columns.FirstOrDefault(item => string.Equals(item.Name, feature, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                continue;
            }

            var item = MapFeature(column, index.RowCount);
            item.CorrelationAbs = Math.Abs(edge.Score);
            output.Add(item);
            if (output.Count >= take)
            {
                break;
            }
        }

        return output;
    }

    private static List<LlmTopFeature> ResolveHighVariance(IReadOnlyCollection<ColumnIndex> columns, long rowCount, int take)
    {
        var varianceValues = columns
            .Select(column => column.NumericStats?.P90 ?? column.NumericStats?.StdDev ?? 0)
            .ToList();

        return columns
            .Select(column =>
            {
                var item = MapFeature(column, rowCount);
                item.VarianceNorm = Normalization.RobustVarianceNormalize(
                    column.NumericStats?.P90 ?? column.NumericStats?.StdDev,
                    varianceValues);
                return item;
            })
            .OrderByDescending(item => item.VarianceNorm)
            .Take(take)
            .ToList();
    }

    private static LlmTopFeature MapFeature(ColumnIndex column, long rowCount)
    {
        return new LlmTopFeature
        {
            Name = column.Name,
            CorrelationAbs = 0,
            NullRate = column.NullRate,
            VarianceNorm = 0,
            CardinalityRatio = rowCount <= 0 ? 0 : column.DistinctCount / (double)rowCount,
            RoleHints = ResolveRoleHints(column),
            SemanticHints = column.SemanticTags.Take(5).ToList()
        };
    }

    private static string ResolveGranularity(ColumnIndex column)
    {
        if (column.DateStats?.Coverage.Count > 20)
        {
            return "daily";
        }

        if (column.DateStats?.Coverage.Count > 5)
        {
            return "monthly";
        }

        return "unknown";
    }

    private static double EstimateOutlierRate(ColumnIndex column)
    {
        if (column.NumericStats?.P95 is null || column.NumericStats?.P50 is null)
        {
            return 0;
        }

        var spread = Math.Abs(column.NumericStats.P95.Value - column.NumericStats.P50.Value);
        var baseline = Math.Abs(column.NumericStats.P50.Value) + 1d;
        return Normalization.Clamp01((spread / baseline) / 10d);
    }

    private static List<string> ResolveRoleHints(ColumnIndex column)
    {
        var hints = new List<string>();
        if (column.DateStats is not null || column.InferredType == InferredType.Date)
        {
            hints.Add("time");
        }

        if (column.NumericStats is not null)
        {
            hints.Add("measure");
        }
        else
        {
            hints.Add("dimension");
        }

        return hints;
    }

    private static string SerializeWithSizeGuard(LlmDatasetProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) <= MaxPayloadBytes)
        {
            return json;
        }

        while (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
        {
            if (profile.TopCorrelatedFeatures.Count > 4)
            {
                profile.TopCorrelatedFeatures = profile.TopCorrelatedFeatures.Take(profile.TopCorrelatedFeatures.Count - 1).ToList();
            }
            else if (profile.HighVarianceFeatures.Count > 4)
            {
                profile.HighVarianceFeatures = profile.HighVarianceFeatures.Take(profile.HighVarianceFeatures.Count - 1).ToList();
            }
            else if (profile.HighNullRateFeatures.Count > 3)
            {
                profile.HighNullRateFeatures = profile.HighNullRateFeatures.Take(profile.HighNullRateFeatures.Count - 1).ToList();
            }
            else if (profile.RecommendedChartsSummary.Count > 6)
            {
                profile.RecommendedChartsSummary = profile.RecommendedChartsSummary.Take(profile.RecommendedChartsSummary.Count - 1).ToList();
            }
            else
            {
                break;
            }

            json = JsonSerializer.Serialize(profile, JsonOptions);
        }

        return json;
    }

    private static string BuildSystemPrompt(string language)
    {
        var outputLanguage = language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? "pt-BR"
            : "en-US";

        return $"""
Você é um analista sênior de dados. Gere resposta APENAS com base no payload fornecido.
Nunca invente colunas. Use nomes de colunas exatamente como no payload.
Se um dado não estiver no payload, diga explicitamente que está indisponível.
Formato obrigatório da resposta:
1) Resumo executivo (3 bullets)
2) Principais drivers (até 5) com números
3) Oportunidades de segmentação (até 3)
4) Riscos/avisos de qualidade (nulos, outliers, viés)
5) Próximas ações recomendadas (3 itens)
Idioma de saída: {outputLanguage}
""";
    }

    private static string BuildUserPrompt(string language, string payloadJson)
    {
        _ = language;
        return $"""
Analise o dataset com base no payload estruturado abaixo.
Não use markdown fora da estrutura pedida.

```json
{payloadJson}
```
""";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
