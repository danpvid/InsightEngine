using System.Text.Json;
using InsightEngine.Domain.Constants;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Application.Services;

public class AIInsightService : IAIInsightService
{
    private readonly ILLMContextBuilder _contextBuilder;
    private readonly ILLMClient _llmClient;
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly ILogger<AIInsightService> _logger;

    public AIInsightService(
        ILLMContextBuilder contextBuilder,
        ILLMClient llmClient,
        IOptionsMonitor<LLMSettings> settingsMonitor,
        ILogger<AIInsightService> logger)
    {
        _contextBuilder = contextBuilder;
        _llmClient = llmClient;
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public async Task<Result<AiInsightSummaryResult>> GenerateAiSummaryAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var language = NormalizeLanguage(request.Language);
        var contextResult = await _contextBuilder.BuildChartContextAsync(request, cancellationToken);
        if (!contextResult.IsSuccess || contextResult.Data == null)
        {
            return Result.Failure<AiInsightSummaryResult>(contextResult.Errors);
        }

        var context = contextResult.Data;
        var llmRequest = new LLMRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            QueryHash = context.QueryHash,
            FeatureKind = $"ai-summary-{language}",
            PromptVersion = LLMPromptVersion.Value,
            ResponseFormat = LLMResponseFormat.Json,
            SystemPrompt = BuildSystemPrompt(language),
            UserPrompt = BuildUserPrompt(language),
            ContextObjects = context.ContextObjects
        };

        var llmResult = await _llmClient.GenerateJsonAsync(llmRequest, cancellationToken);
        if (llmResult.IsSuccess && llmResult.Data != null)
        {
            var generated = TryParseSummary(llmResult.Data.Json ?? llmResult.Data.Text);
            if (generated != null)
            {
                return Result.Success(new AiInsightSummaryResult
                {
                    InsightSummary = generated,
                    Meta = new AiGenerationMeta
                    {
                        Provider = llmResult.Data.Provider,
                        Model = llmResult.Data.ModelId,
                        DurationMs = llmResult.Data.DurationMs,
                        CacheHit = llmResult.Data.CacheHit,
                        FallbackUsed = false
                    }
                });
            }

            _logger.LogWarning(
                "AI summary JSON validation failed for dataset {DatasetId}, recommendation {RecommendationId}.",
                request.DatasetId,
                request.RecommendationId);
        }
        else
        {
            _logger.LogWarning(
                "AI summary generation failed for dataset {DatasetId}, recommendation {RecommendationId}. Errors={Errors}",
                request.DatasetId,
                request.RecommendationId,
                string.Join(" | ", llmResult.Errors));
        }

        return Result.Success(new AiInsightSummaryResult
        {
            InsightSummary = BuildFallbackSummary(context.HeuristicSummary, language),
            Meta = new AiGenerationMeta
            {
                Provider = llmResult.Data?.Provider ?? LLMProvider.None,
                Model = llmResult.Data?.ModelId ?? "heuristic",
                DurationMs = llmResult.Data?.DurationMs ?? 0,
                CacheHit = llmResult.Data?.CacheHit ?? false,
                FallbackUsed = true,
                FallbackReason = llmResult.IsSuccess
                    ? "Invalid AI JSON output; using heuristic summary."
                    : string.Join(" | ", llmResult.Errors)
            }
        });
    }

    public async Task<Result<AskAnalysisPlanResult>> AskAnalysisPlanAsync(
        AskAnalysisPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var language = NormalizeLanguage(request.Language);
        if (request.DatasetId == Guid.Empty)
        {
            return Result.Failure<AskAnalysisPlanResult>("DatasetId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return Result.Failure<AskAnalysisPlanResult>("Question is required.");
        }

        var maxQuestionChars = Math.Max(100, _settingsMonitor.CurrentValue.AskMaxQuestionChars);
        if (request.Question.Length > maxQuestionChars)
        {
            return Result.Failure<AskAnalysisPlanResult>($"Question exceeds max length of {maxQuestionChars} characters.");
        }

        var contextResult = await _contextBuilder.BuildAskContextAsync(
            new LLMAskContextRequest
            {
                DatasetId = request.DatasetId,
                Language = language,
                CurrentView = request.CurrentView
            },
            cancellationToken);

        if (!contextResult.IsSuccess || contextResult.Data == null)
        {
            return Result.Failure<AskAnalysisPlanResult>(contextResult.Errors);
        }

        var context = contextResult.Data;
        var llmRequest = new LLMRequest
        {
            DatasetId = request.DatasetId,
            QueryHash = context.QueryHash,
            FeatureKind = $"ask-plan-{language}",
            PromptVersion = LLMPromptVersion.Value,
            ResponseFormat = LLMResponseFormat.Json,
            SystemPrompt = BuildAskSystemPrompt(language),
            UserPrompt = BuildAskUserPrompt(request.Question, language),
            ContextObjects = context.ContextObjects
        };

        var llmResult = await _llmClient.GenerateJsonAsync(llmRequest, cancellationToken);
        if (llmResult.IsSuccess && llmResult.Data != null)
        {
            var parsed = TryParseAnalysisPlan(llmResult.Data.Json ?? llmResult.Data.Text);
            if (parsed != null)
            {
                return Result.Success(new AskAnalysisPlanResult
                {
                    Plan = parsed,
                    Meta = new AiGenerationMeta
                    {
                        Provider = llmResult.Data.Provider,
                        Model = llmResult.Data.ModelId,
                        DurationMs = llmResult.Data.DurationMs,
                        CacheHit = llmResult.Data.CacheHit,
                        FallbackUsed = false
                    }
                });
            }
        }

        return Result.Success(new AskAnalysisPlanResult
        {
            Plan = BuildFallbackPlan(request.Question, language),
            Meta = new AiGenerationMeta
            {
                Provider = llmResult.Data?.Provider ?? LLMProvider.None,
                Model = llmResult.Data?.ModelId ?? "heuristic",
                DurationMs = llmResult.Data?.DurationMs ?? 0,
                CacheHit = llmResult.Data?.CacheHit ?? false,
                FallbackUsed = true,
                FallbackReason = llmResult.IsSuccess
                    ? "Invalid AI JSON output; returning fallback plan."
                    : string.Join(" | ", llmResult.Errors)
            }
        });
    }

    public async Task<Result<ChartExplanationResult>> ExplainChartAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default)
    {
        var language = NormalizeLanguage(request.Language);
        var contextResult = await _contextBuilder.BuildChartContextAsync(request, cancellationToken);
        if (!contextResult.IsSuccess || contextResult.Data == null)
        {
            return Result.Failure<ChartExplanationResult>(contextResult.Errors);
        }

        var context = contextResult.Data;
        var llmRequest = new LLMRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            QueryHash = context.QueryHash,
            FeatureKind = $"explain-chart-{language}",
            PromptVersion = LLMPromptVersion.Value,
            ResponseFormat = LLMResponseFormat.Json,
            SystemPrompt = BuildExplainSystemPrompt(language),
            UserPrompt = BuildExplainUserPrompt(language),
            ContextObjects = context.ContextObjects
        };

        var llmResult = await _llmClient.GenerateJsonAsync(llmRequest, cancellationToken);
        if (llmResult.IsSuccess && llmResult.Data != null)
        {
            var parsed = TryParseExplanation(llmResult.Data.Json ?? llmResult.Data.Text);
            if (parsed != null)
            {
                return Result.Success(new ChartExplanationResult
                {
                    Explanation = parsed,
                    Meta = new AiGenerationMeta
                    {
                        Provider = llmResult.Data.Provider,
                        Model = llmResult.Data.ModelId,
                        DurationMs = llmResult.Data.DurationMs,
                        CacheHit = llmResult.Data.CacheHit,
                        FallbackUsed = false
                    }
                });
            }
        }

        return Result.Success(new ChartExplanationResult
        {
            Explanation = BuildFallbackExplanation(context.HeuristicSummary, language),
            Meta = new AiGenerationMeta
            {
                Provider = llmResult.Data?.Provider ?? LLMProvider.None,
                Model = llmResult.Data?.ModelId ?? "heuristic",
                DurationMs = llmResult.Data?.DurationMs ?? 0,
                CacheHit = llmResult.Data?.CacheHit ?? false,
                FallbackUsed = true,
                FallbackReason = llmResult.IsSuccess
                    ? "Invalid AI JSON output; using heuristic explanation."
                    : string.Join(" | ", llmResult.Errors)
            }
        });
    }

    private static AiInsightSummary? TryParseSummary(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AiInsightSummary>(payload, SerializerOptions);
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.Headline))
            {
                return null;
            }

            if (parsed.BulletPoints.Count == 0)
            {
                return null;
            }

            parsed.Cautions ??= new List<string>();
            parsed.NextQuestions ??= new List<string>();
            parsed.Confidence = Math.Clamp(parsed.Confidence, 0.0, 1.0);

            parsed.BulletPoints = parsed.BulletPoints
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Take(6)
                .Select(point => point.Trim())
                .ToList();
            parsed.Cautions = parsed.Cautions
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Take(4)
                .Select(point => point.Trim())
                .ToList();
            parsed.NextQuestions = parsed.NextQuestions
                .Where(point => !string.IsNullOrWhiteSpace(point))
                .Take(5)
                .Select(point => point.Trim())
                .ToList();

            return parsed.BulletPoints.Count == 0 ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AiInsightSummary BuildFallbackSummary(InsightSummary? heuristic, string language)
    {
        var portuguese = IsPortuguese(language);
        if (heuristic == null)
        {
            return new AiInsightSummary
            {
                Headline = portuguese ? "Resumo de IA indisponível" : "AI summary unavailable",
                BulletPoints =
                [
                    portuguese
                        ? "Não há informação suficiente para gerar um resumo de IA confiável agora."
                        : "There is not enough information to build a reliable AI summary right now."
                ],
                Cautions =
                [
                    portuguese
                        ? "Este fallback não inclui interpretação adicional da IA."
                        : "This fallback does not include additional AI interpretation."
                ],
                NextQuestions =
                [
                    portuguese
                        ? "Você pode ajustar agregação ou filtros e tentar novamente?"
                        : "Can you adjust aggregation or filters and try again?"
                ],
                Confidence = 0.2
            };
        }

        var nextQuestions = portuguese
            ? new List<string>
            {
                "Devemos comparar esta métrica em outra dimensão?",
                "Os outliers persistem após aplicar um intervalo de tempo menor?"
            }
            : new List<string>
            {
                "Should we compare this metric across another dimension?",
                "Do outliers persist after applying a narrower time range?"
            };

        return new AiInsightSummary
        {
            Headline = heuristic.Headline,
            BulletPoints = heuristic.BulletPoints.Take(6).ToList(),
            Cautions =
            [
                portuguese
                    ? "A saída da IA não estava disponível. Exibindo resumo heurístico."
                    : "AI output was unavailable. Showing heuristic insight summary."
            ],
            NextQuestions = nextQuestions,
            Confidence = Math.Clamp(heuristic.Confidence, 0.0, 1.0)
        };
    }

    private static ChartExplanation? TryParseExplanation(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ChartExplanation>(payload, SerializerOptions);
            if (parsed == null)
            {
                return null;
            }

            parsed.Explanation = NormalizeList(parsed.Explanation, 4);
            parsed.KeyTakeaways = NormalizeList(parsed.KeyTakeaways, 6);
            parsed.PotentialCauses = NormalizeList(parsed.PotentialCauses, 6);
            parsed.Caveats = NormalizeList(parsed.Caveats, 5);
            parsed.SuggestedNextSteps = NormalizeList(parsed.SuggestedNextSteps, 5);
            parsed.QuestionsToAsk = NormalizeList(parsed.QuestionsToAsk, 6);

            if (parsed.Explanation.Count == 0 || parsed.KeyTakeaways.Count == 0)
            {
                return null;
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChartExplanation BuildFallbackExplanation(InsightSummary? heuristic, string language)
    {
        var portuguese = IsPortuguese(language);
        if (heuristic == null)
        {
            return new ChartExplanation
            {
                Explanation =
                [
                    portuguese
                        ? "A explicação por IA está indisponível para este gráfico no ambiente atual."
                        : "AI explanation is unavailable for this chart in the current environment."
                ],
                KeyTakeaways =
                [
                    portuguese
                        ? "Tente ajustar filtros ou agregação e solicite a explicação novamente."
                        : "Try adjusting filters or aggregation and request explanation again."
                ],
                Caveats =
                [
                    portuguese
                        ? "A explicação de fallback é baseada em contexto heurístico limitado."
                        : "Fallback explanation is based on limited heuristic context."
                ],
                SuggestedNextSteps =
                [
                    portuguese
                        ? "Execute o gráfico com outro período."
                        : "Run the chart with a different time range.",
                    portuguese
                        ? "Compare com uma segunda métrica."
                        : "Compare with a second metric."
                ],
                QuestionsToAsk =
                [
                    portuguese
                        ? "Qual segmento mais contribui para a mudança observada?"
                        : "Which segment contributes most to the observed change?"
                ]
            };
        }

        return new ChartExplanation
        {
            Explanation =
            [
                heuristic.Headline
            ],
            KeyTakeaways = heuristic.BulletPoints.Take(6).ToList(),
            PotentialCauses =
            [
                portuguese
                    ? "A variação pode ser causada por mix de categorias, sazonalidade ou concentração de outliers."
                    : "Variance may be driven by category mix, seasonality, or outlier concentration.",
                portuguese
                    ? "Revise quebras por dimensão para validar os principais direcionadores."
                    : "Review dimension-level breakdowns to validate likely drivers."
            ],
            Caveats =
            [
                portuguese
                    ? "Fallback de explicação por IA ativo; esta saída usa regras heurísticas."
                    : "AI explanation fallback is active; this output uses heuristic rules.",
                portuguese
                    ? "Correlação não implica causalidade."
                    : "Correlation does not imply causation."
            ],
            SuggestedNextSteps =
            [
                portuguese
                    ? "Quebre a métrica por groupBy para isolar os principais contribuintes."
                    : "Break down the metric by groupBy to isolate contributors.",
                portuguese
                    ? "Aplique filtros de drilldown em torno de pontos anômalos."
                    : "Apply drilldown filters around anomalous points."
            ],
            QuestionsToAsk =
            [
                portuguese
                    ? "O mesmo padrão se mantém em outra janela de tempo?"
                    : "Does the same pattern hold in another time window?",
                portuguese
                    ? "Os outliers estão concentrados em um segmento específico?"
                    : "Are outliers concentrated in a specific segment?"
            ]
        };
    }

    private static AskAnalysisPlan? TryParseAnalysisPlan(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AskAnalysisPlan>(payload, SerializerOptions);
            if (parsed == null)
            {
                return null;
            }

            parsed.Intent = NormalizeIntent(parsed.Intent);
            parsed.SuggestedChartType = NormalizeChartType(parsed.SuggestedChartType);
            parsed.Reasoning = NormalizeList(parsed.Reasoning, 6);
            parsed.SuggestedFilters ??= new List<AskSuggestedFilter>();
            parsed.SuggestedFilters = parsed.SuggestedFilters
                .Where(filter => !string.IsNullOrWhiteSpace(filter.Column))
                .Take(3)
                .Select(filter => new AskSuggestedFilter
                {
                    Column = filter.Column.Trim(),
                    Operator = NormalizeFilterOperator(filter.Operator),
                    Values = filter.Values
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Take(3)
                        .ToList()
                })
                .ToList();

            return parsed.Reasoning.Count == 0 ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AskAnalysisPlan BuildFallbackPlan(string question, string language)
    {
        var portuguese = IsPortuguese(language);
        var lowered = question.ToLowerInvariant();
        var intent = lowered.Contains("trend", StringComparison.OrdinalIgnoreCase)
            || lowered.Contains("tend", StringComparison.OrdinalIgnoreCase)
            ? "Trend"
            : lowered.Contains("outlier", StringComparison.OrdinalIgnoreCase)
                || lowered.Contains("anom", StringComparison.OrdinalIgnoreCase)
                ? "Outliers"
                : lowered.Contains("break", StringComparison.OrdinalIgnoreCase)
                    || lowered.Contains("quebra", StringComparison.OrdinalIgnoreCase)
                    ? "Breakdown"
                    : "Compare";

        var chartType = intent == "Trend" ? "Line" : "Bar";

        return new AskAnalysisPlan
        {
            Intent = intent,
            SuggestedChartType = chartType,
            Reasoning =
            [
                portuguese
                    ? "O planejamento de fallback está ativo porque a saída de IA está indisponível."
                    : "Fallback planning is active because AI output was unavailable.",
                portuguese
                    ? "O plano usa detecção de intenção por palavras-chave e o schema atual do dataset."
                    : "The plan uses keyword intent detection and current dataset schema."
            ]
        };
    }

    private static string BuildSystemPrompt(string language)
    {
        return """
You are a data insight assistant. Return valid JSON only.
Do not include markdown or prose outside JSON.
Keep language business-friendly and concise.
Include limitations and assumptions in cautions when uncertainty exists.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildExplainSystemPrompt(string language)
    {
        return """
You are a business analytics assistant.
Return JSON only. No markdown.
Use plain business language and avoid SQL/database jargon.
Include caveats when evidence is weak.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildUserPrompt(string language)
    {
        return """
Generate an insight summary from the provided chart context.
Return JSON that strictly follows this schema:
{
  "headline": "string",
  "bulletPoints": ["string"],
  "cautions": ["string"],
  "nextQuestions": ["string"],
  "confidence": 0.0
}
Rules:
- bulletPoints: 3 to 6 concise bullets.
- cautions: 0 to 4 bullets with assumptions/limitations.
- nextQuestions: 2 to 5 actionable follow-up questions.
- confidence: number between 0 and 1.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildExplainUserPrompt(string language)
    {
        return """
Explain the chart context and return JSON using this schema:
{
  "explanation": ["string"],
  "keyTakeaways": ["string"],
  "potentialCauses": ["string"],
  "caveats": ["string"],
  "suggestedNextSteps": ["string"],
  "questionsToAsk": ["string"]
}
Rules:
- explanation: 1 to 3 short paragraphs.
- keyTakeaways: 3 to 6 bullets.
- potentialCauses: 1 to 5 bullets.
- caveats: 1 to 4 bullets.
- suggestedNextSteps: 2 to 5 bullets.
- questionsToAsk: 2 to 6 bullets.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildAskSystemPrompt(string language)
    {
        return """
You convert user questions into a chart analysis plan.
Return JSON only.
Do not produce SQL.
Use one of these intents: Compare, Trend, Breakdown, Outliers.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildAskUserPrompt(string question, string language)
    {
        return string.Join('\n',
            "User question:",
            question,
            string.Empty,
            "Return JSON with this schema:",
            "{",
            "  \"intent\": \"Compare|Trend|Breakdown|Outliers\",",
            "  \"suggestedChartType\": \"Line|Bar|Scatter|Histogram\",",
            "  \"proposedDimensions\": {",
            "    \"x\": \"string or null\",",
            "    \"y\": \"string or null\",",
            "    \"groupBy\": \"string or null\"",
            "  },",
            "  \"suggestedFilters\": [",
            "    {",
            "      \"column\": \"string\",",
            "      \"operator\": \"Eq|NotEq|In|Between|Contains\",",
            "      \"values\": [\"string\"]",
            "    }",
            "  ],",
            "  \"reasoning\": [\"string\"]",
            "}",
            string.Empty,
            BuildOutputLanguageInstruction(language));
    }

    private static bool IsPortuguese(string language) =>
        string.Equals(NormalizeLanguage(language), "pt-br", StringComparison.Ordinal);

    private static string NormalizeLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pt" => "pt-br",
            "pt-br" => "pt-br",
            "en" => "en",
            "en-us" => "en",
            _ => "pt-br"
        };
    }

    private static string BuildOutputLanguageInstruction(string language) =>
        IsPortuguese(language)
            ? "All user-facing strings in JSON must be Brazilian Portuguese (pt-BR)."
            : "All user-facing strings in JSON must be English (en).";

    private static List<string> NormalizeList(List<string>? values, int maxItems)
    {
        if (values == null || values.Count == 0)
        {
            return new List<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Take(maxItems)
            .ToList();
    }

    private static string NormalizeIntent(string? intent)
    {
        return intent?.Trim().ToLowerInvariant() switch
        {
            "trend" => "Trend",
            "breakdown" => "Breakdown",
            "outliers" => "Outliers",
            _ => "Compare"
        };
    }

    private static string NormalizeChartType(string? chartType)
    {
        return chartType?.Trim().ToLowerInvariant() switch
        {
            "line" => "Line",
            "scatter" => "Scatter",
            "histogram" => "Histogram",
            _ => "Bar"
        };
    }

    private static string NormalizeFilterOperator(string? filterOperator)
    {
        return filterOperator?.Trim().ToLowerInvariant() switch
        {
            "noteq" => "NotEq",
            "in" => "In",
            "between" => "Between",
            "contains" => "Contains",
            _ => "Eq"
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
