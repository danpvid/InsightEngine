using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    private readonly IEvidencePackService _evidencePackService;
    private readonly ILLMClient _llmClient;
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly ILogger<AIInsightService> _logger;

    public AIInsightService(
        ILLMContextBuilder contextBuilder,
        IEvidencePackService evidencePackService,
        ILLMClient llmClient,
        IOptionsMonitor<LLMSettings> settingsMonitor,
        ILogger<AIInsightService> logger)
    {
        _contextBuilder = contextBuilder;
        _evidencePackService = evidencePackService;
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

    public async Task<Result<DeepInsightsResult>> GenerateDeepInsightsAsync(
        DeepInsightsRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var language = NormalizeLanguage(request.Language);
        var budgetCheck = TryConsumeDeepInsightsBudget(request);
        if (!budgetCheck.IsAllowed)
        {
            return Result.Failure<DeepInsightsResult>(budgetCheck.Reason ?? "Deep insights request limit reached.");
        }

        var evidenceResult = await _evidencePackService.BuildEvidencePackAsync(request, cancellationToken);
        if (!evidenceResult.IsSuccess || evidenceResult.Data == null)
        {
            return Result.Failure<DeepInsightsResult>(evidenceResult.Errors);
        }

        var evidencePack = evidenceResult.Data.EvidencePack;
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["evidencePack"] = evidencePack
        };

        var llmRequest = new LLMRequest
        {
            DatasetId = request.DatasetId,
            RecommendationId = request.RecommendationId,
            QueryHash = evidencePack.QueryHash,
            FeatureKind = $"deep-insights-{language}",
            PromptVersion = $"{LLMPromptVersion.Value}-{evidencePack.EvidenceVersion}",
            ResponseFormat = LLMResponseFormat.Json,
            SystemPrompt = BuildDeepInsightsSystemPrompt(language),
            UserPrompt = BuildDeepInsightsUserPrompt(language),
            ContextObjects = context
        };

        var llmResult = await _llmClient.GenerateJsonAsync(llmRequest, cancellationToken);
        if (llmResult.IsSuccess && llmResult.Data != null)
        {
            var parsed = TryParseDeepInsightsReport(llmResult.Data.Json ?? llmResult.Data.Text);
            string? validationError = null;
            if (parsed != null &&
                TryValidateDeepInsightReport(parsed, evidencePack.Facts.Select(f => f.EvidenceId), out var explainability, out validationError))
            {
                parsed.Meta = new DeepInsightMeta
                {
                    Provider = llmResult.Data.Provider.ToString(),
                    Model = llmResult.Data.ModelId,
                    PromptVersion = llmRequest.PromptVersion,
                    EvidenceVersion = evidencePack.EvidenceVersion
                };

                var successResult = new DeepInsightsResult
                {
                    Report = parsed,
                    Meta = new AiGenerationMeta
                    {
                        Provider = llmResult.Data.Provider,
                        Model = llmResult.Data.ModelId,
                        DurationMs = llmResult.Data.DurationMs,
                        CacheHit = llmResult.Data.CacheHit,
                        FallbackUsed = false,
                        EvidenceBytes = evidencePack.SerializedBytes,
                        OutputBytes = Encoding.UTF8.GetByteCount(llmResult.Data.Json ?? llmResult.Data.Text ?? string.Empty),
                        ValidationStatus = "ok"
                    },
                    Explainability = explainability,
                    EvidencePack = evidencePack
                };

                stopwatch.Stop();
                _logger.LogInformation(
                    "Deep insights generated DatasetId={DatasetId} RecommendationId={RecommendationId} QueryHash={QueryHash} featureKind=deep-insights Provider={Provider} Model={Model} DurationMs={DurationMs} TotalMs={TotalMs} cacheHit={CacheHit} evidenceBytes={EvidenceBytes} outputBytes={OutputBytes} validationStatus={ValidationStatus}",
                    request.DatasetId,
                    request.RecommendationId,
                    evidencePack.QueryHash,
                    successResult.Meta.Provider,
                    successResult.Meta.Model,
                    successResult.Meta.DurationMs,
                    stopwatch.ElapsedMilliseconds,
                    successResult.Meta.CacheHit,
                    successResult.Meta.EvidenceBytes,
                    successResult.Meta.OutputBytes,
                    successResult.Meta.ValidationStatus);

                return Result.Success(successResult);
            }

            _logger.LogWarning(
                "Deep insights validation failed for dataset {DatasetId}, recommendation {RecommendationId}. Error={Error}",
                request.DatasetId,
                request.RecommendationId,
                validationError ?? "unknown");
        }
        else
        {
            _logger.LogWarning(
                "Deep insights generation failed for dataset {DatasetId}, recommendation {RecommendationId}. Errors={Errors}",
                request.DatasetId,
                request.RecommendationId,
                string.Join(" | ", llmResult.Errors));
        }

        var fallbackResult = new DeepInsightsResult
        {
            Report = BuildFallbackDeepInsightsReport(evidencePack, language),
            Meta = new AiGenerationMeta
            {
                Provider = llmResult.Data?.Provider ?? LLMProvider.None,
                Model = llmResult.Data?.ModelId ?? "heuristic",
                DurationMs = llmResult.Data?.DurationMs ?? 0,
                CacheHit = llmResult.Data?.CacheHit ?? evidenceResult.Data.CacheHit,
                FallbackUsed = true,
                FallbackReason = llmResult.IsSuccess
                    ? "Invalid deep insights JSON output; using deterministic fallback."
                    : string.Join(" | ", llmResult.Errors),
                EvidenceBytes = evidencePack.SerializedBytes,
                OutputBytes = Encoding.UTF8.GetByteCount(llmResult.Data?.Json ?? llmResult.Data?.Text ?? string.Empty),
                ValidationStatus = llmResult.IsSuccess ? "invalid" : "fallback"
            },
            Explainability = BuildExplainabilityFromFallback(evidencePack),
            EvidencePack = evidencePack
        };

        stopwatch.Stop();
        _logger.LogInformation(
            "Deep insights fallback DatasetId={DatasetId} RecommendationId={RecommendationId} QueryHash={QueryHash} featureKind=deep-insights Provider={Provider} Model={Model} DurationMs={DurationMs} TotalMs={TotalMs} cacheHit={CacheHit} evidenceBytes={EvidenceBytes} outputBytes={OutputBytes} validationStatus={ValidationStatus}",
            request.DatasetId,
            request.RecommendationId,
            evidencePack.QueryHash,
            fallbackResult.Meta.Provider,
            fallbackResult.Meta.Model,
            fallbackResult.Meta.DurationMs,
            stopwatch.ElapsedMilliseconds,
            fallbackResult.Meta.CacheHit,
            fallbackResult.Meta.EvidenceBytes,
            fallbackResult.Meta.OutputBytes,
            fallbackResult.Meta.ValidationStatus);

        return Result.Success(fallbackResult);
    }

    private static DeepInsightReport? TryParseDeepInsightsReport(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<DeepInsightReport>(payload, SerializerOptions);
            if (parsed == null)
            {
                return null;
            }

            parsed.Headline = (parsed.Headline ?? string.Empty).Trim();
            parsed.ExecutiveSummary = (parsed.ExecutiveSummary ?? string.Empty).Trim();
            parsed.KeyFindings ??= new List<DeepInsightFinding>();
            parsed.Drivers ??= new List<DeepInsightDriver>();
            parsed.RisksAndCaveats ??= new List<DeepInsightRisk>();
            parsed.RecommendedActions ??= new List<DeepInsightAction>();
            parsed.NextQuestions ??= new List<string>();
            parsed.Citations ??= new List<DeepInsightCitation>();
            parsed.Projections ??= new DeepInsightProjectionSection();
            parsed.Projections.Methods ??= new List<DeepInsightProjectionMethod>();
            parsed.Meta ??= new DeepInsightMeta();

            parsed.KeyFindings = parsed.KeyFindings.Take(7).ToList();
            parsed.Drivers = parsed.Drivers.Take(6).ToList();
            parsed.RisksAndCaveats = parsed.RisksAndCaveats.Take(6).ToList();
            parsed.RecommendedActions = parsed.RecommendedActions.Take(6).ToList();
            parsed.NextQuestions = NormalizeList(parsed.NextQuestions, 8);
            parsed.Citations = parsed.Citations
                .Where(item => !string.IsNullOrWhiteSpace(item.EvidenceId) && !string.IsNullOrWhiteSpace(item.ShortClaim))
                .Take(20)
                .ToList();

            foreach (var finding in parsed.KeyFindings)
            {
                finding.Title = finding.Title.Trim();
                finding.Narrative = finding.Narrative.Trim();
                finding.Severity = NormalizeRiskLevel(finding.Severity, "medium");
                finding.EvidenceIds = NormalizeList(finding.EvidenceIds, 8);
            }

            foreach (var driver in parsed.Drivers)
            {
                driver.Driver = driver.Driver.Trim();
                driver.WhyItMatters = driver.WhyItMatters.Trim();
                driver.EvidenceIds = NormalizeList(driver.EvidenceIds, 8);
            }

            foreach (var risk in parsed.RisksAndCaveats)
            {
                risk.Risk = risk.Risk.Trim();
                risk.Mitigation = risk.Mitigation.Trim();
                risk.EvidenceIds = NormalizeList(risk.EvidenceIds, 8);
            }

            foreach (var action in parsed.RecommendedActions)
            {
                action.Action = action.Action.Trim();
                action.ExpectedImpact = action.ExpectedImpact.Trim();
                action.Effort = NormalizeRiskLevel(action.Effort, "medium");
                action.EvidenceIds = NormalizeList(action.EvidenceIds, 8);
            }

            foreach (var method in parsed.Projections.Methods)
            {
                method.Method = NormalizeProjectionMethod(method.Method);
                method.Narrative = method.Narrative.Trim();
                method.Confidence = NormalizeRiskLevel(method.Confidence, "medium");
                method.EvidenceIds = NormalizeList(method.EvidenceIds, 8);
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryValidateDeepInsightReport(
        DeepInsightReport report,
        IEnumerable<string> availableEvidenceIds,
        out DeepInsightsExplainability explainability,
        out string? validationError)
    {
        explainability = new DeepInsightsExplainability();
        validationError = null;

        if (string.IsNullOrWhiteSpace(report.Headline) || report.Headline.Length > 120)
        {
            validationError = "Headline is missing or too long.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(report.ExecutiveSummary) || report.ExecutiveSummary.Length > 600)
        {
            validationError = "Executive summary is missing or too long.";
            return false;
        }

        if (report.KeyFindings.Count == 0)
        {
            validationError = "At least one key finding is required.";
            return false;
        }

        var allowed = new HashSet<string>(
            availableEvidenceIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            validationError = "No evidence ids available for validation.";
            return false;
        }

        var usedIds = new List<string>();
        void CaptureIds(IEnumerable<string> ids)
        {
            usedIds.AddRange(ids.Where(id => !string.IsNullOrWhiteSpace(id)));
        }

        foreach (var finding in report.KeyFindings)
        {
            if (string.IsNullOrWhiteSpace(finding.Title) || string.IsNullOrWhiteSpace(finding.Narrative))
            {
                validationError = "Key findings must include title and narrative.";
                return false;
            }

            CaptureIds(finding.EvidenceIds);
        }

        foreach (var driver in report.Drivers)
        {
            CaptureIds(driver.EvidenceIds);
        }

        foreach (var risk in report.RisksAndCaveats)
        {
            CaptureIds(risk.EvidenceIds);
        }

        foreach (var method in report.Projections.Methods)
        {
            CaptureIds(method.EvidenceIds);
        }

        foreach (var action in report.RecommendedActions)
        {
            CaptureIds(action.EvidenceIds);
        }

        foreach (var citation in report.Citations)
        {
            usedIds.Add(citation.EvidenceId);
        }

        var missing = usedIds
            .Where(id => !allowed.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count > 0)
        {
            validationError = $"Unknown evidence ids: {string.Join(", ", missing.Take(5))}.";
            return false;
        }

        explainability = new DeepInsightsExplainability
        {
            EvidenceUsedCount = usedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TopEvidenceIdsUsed = usedIds
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .Take(10)
                .Select(group => group.Key)
                .ToList()
        };

        return true;
    }

    private static DeepInsightReport BuildFallbackDeepInsightsReport(EvidencePack evidencePack, string language)
    {
        var portuguese = IsPortuguese(language);
        var distribution = evidencePack.DistributionStats.FirstOrDefault();
        var topEvidence = evidencePack.Facts.Select(f => f.EvidenceId).Take(8).ToList();
        var trend = evidencePack.TimeSeriesStats?.TrendClassification ?? (portuguese ? "Indefinida" : "Undefined");
        var meanValue = distribution != null ? distribution.Mean.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        var p95Value = distribution != null ? distribution.P95.ToString("0.###", CultureInfo.InvariantCulture) : "-";

        var headline = portuguese
            ? $"Leitura analítica ({trend})"
            : $"Analytical readout ({trend})";
        var executiveSummary = portuguese
            ? $"Resumo determinístico gerado com evidências calculadas. Média observada: {meanValue}; P95: {p95Value}. Use esta saída enquanto o LLM está indisponível."
            : $"Deterministic summary generated from computed evidence. Observed mean: {meanValue}; P95: {p95Value}. Use this output while LLM is unavailable.";

        return new DeepInsightReport
        {
            Headline = headline,
            ExecutiveSummary = executiveSummary,
            KeyFindings =
            [
                new DeepInsightFinding
                {
                    Title = portuguese ? "Sinal principal" : "Primary signal",
                    Narrative = portuguese
                        ? $"A tendência atual foi classificada como {trend} com base nos pontos agregados."
                        : $"The current trend was classified as {trend} based on aggregated points.",
                    Severity = "medium",
                    EvidenceIds = topEvidence.Take(2).ToList()
                },
                new DeepInsightFinding
                {
                    Title = portuguese ? "Distribuição" : "Distribution",
                    Narrative = portuguese
                        ? $"A distribuição apresenta média {meanValue} e cauda superior (P95) em {p95Value}."
                        : $"The distribution shows mean {meanValue} with upper tail (P95) at {p95Value}.",
                    Severity = "medium",
                    EvidenceIds = topEvidence.Skip(2).Take(2).ToList()
                }
            ],
            Drivers =
            [
                new DeepInsightDriver
                {
                    Driver = portuguese ? "Mix de segmentos" : "Segment mix",
                    WhyItMatters = portuguese
                        ? "Segmentos com maior contribuição concentram o resultado total."
                        : "Top contributing segments concentrate the total outcome.",
                    EvidenceIds = topEvidence.Skip(4).Take(2).ToList()
                }
            ],
            RisksAndCaveats =
            [
                new DeepInsightRisk
                {
                    Risk = portuguese
                        ? "Saída de fallback ativa (LLM indisponível ou inválido)."
                        : "Fallback output active (LLM unavailable or invalid).",
                    Mitigation = portuguese
                        ? "Regerar com o LLM ativo e revisar evidências."
                        : "Regenerate with LLM enabled and review evidence.",
                    EvidenceIds = topEvidence.Skip(6).Take(2).ToList()
                }
            ],
            Projections = new DeepInsightProjectionSection
            {
                Horizon = evidencePack.ForecastPack.Horizon.ToString(CultureInfo.InvariantCulture),
                Methods = evidencePack.ForecastPack.Methods.Select(method => new DeepInsightProjectionMethod
                {
                    Method = NormalizeProjectionMethod(method.Method),
                    Narrative = portuguese
                        ? "Projeção baseline calculada deterministicamente; use como referência inicial."
                        : "Deterministic baseline projection; use it as a starting reference.",
                    Confidence = method.Rmse > 0.2 ? "low" : "medium",
                    EvidenceIds = evidencePack.Facts
                        .Where(f => f.EvidenceId.Contains($"FORECAST_{NormalizeId(method.Method)}", StringComparison.OrdinalIgnoreCase))
                        .Select(f => f.EvidenceId)
                        .Take(3)
                        .ToList()
                }).ToList(),
                Conclusion = portuguese
                    ? "As projeções são baseline e não garantem resultado futuro."
                    : "Projections are baseline estimates and not future guarantees."
            },
            RecommendedActions =
            [
                new DeepInsightAction
                {
                    Action = portuguese
                        ? "Priorizar segmentos com maior share e monitorar desvios."
                        : "Prioritize high-share segments and monitor deviations.",
                    ExpectedImpact = portuguese
                        ? "Maior controle sobre drivers de resultado."
                        : "Improved control over key outcome drivers.",
                    Effort = "medium",
                    EvidenceIds = topEvidence.Take(3).ToList()
                }
            ],
            NextQuestions = portuguese
                ? new List<string>
                {
                    "Quais segmentos sustentam a tendência observada?",
                    "Como a projeção muda com filtros mais restritivos?"
                }
                : new List<string>
                {
                    "Which segments sustain the observed trend?",
                    "How does the projection change with stricter filters?"
                },
            Citations = evidencePack.Facts
                .Take(8)
                .Select(fact => new DeepInsightCitation
                {
                    EvidenceId = fact.EvidenceId,
                    ShortClaim = fact.ShortClaim
                })
                .ToList(),
            Meta = new DeepInsightMeta
            {
                Provider = "None",
                Model = "heuristic",
                PromptVersion = LLMPromptVersion.Value,
                EvidenceVersion = evidencePack.EvidenceVersion
            }
        };
    }

    private static DeepInsightsExplainability BuildExplainabilityFromFallback(EvidencePack evidencePack)
    {
        return new DeepInsightsExplainability
        {
            EvidenceUsedCount = evidencePack.Facts.Count,
            TopEvidenceIdsUsed = evidencePack.Facts.Select(f => f.EvidenceId).Take(10).ToList()
        };
    }

    private static string BuildDeepInsightsSystemPrompt(string language)
    {
        return """
You are a senior business data analyst.
Return strict JSON only. No markdown.
Use only the Evidence Pack facts provided in context.
Do not invent numbers or percentages. If evidence is missing, state "unknown".
Keep language business-friendly and avoid technical jargon such as SQL/ETL.
Every numeric or factual statement must cite evidenceIds.
Do not extrapolate beyond the projections section.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private static string BuildDeepInsightsUserPrompt(string language)
    {
        return """
Generate a deep analytical report with this strict JSON schema:
{
  "headline": "string <= 120 chars",
  "executiveSummary": "string <= 600 chars",
  "keyFindings": [
    { "title": "string", "narrative": "string", "evidenceIds": ["string"], "severity": "low|medium|high" }
  ],
  "drivers": [
    { "driver": "string", "whyItMatters": "string", "evidenceIds": ["string"] }
  ],
  "risksAndCaveats": [
    { "risk": "string", "mitigation": "string", "evidenceIds": ["string"] }
  ],
  "projections": {
    "horizon": "string",
    "methods": [
      { "method": "naive|movingAverage|linearRegression", "narrative": "string", "confidence": "low|medium|high", "evidenceIds": ["string"] }
    ],
    "conclusion": "string"
  },
  "recommendedActions": [
    { "action": "string", "expectedImpact": "string", "effort": "low|medium|high", "evidenceIds": ["string"] }
  ],
  "nextQuestions": ["string"],
  "citations": [
    { "evidenceId": "string", "shortClaim": "string" }
  ],
  "meta": { "provider": "string", "model": "string", "promptVersion": "string", "evidenceVersion": "string" }
}
Rules:
- Keep executiveSummary concise.
- keyFindings length between 3 and 7.
- nextQuestions max 8.
- All referenced evidenceIds must exist in the evidence pack.
"""
        + Environment.NewLine
        + BuildOutputLanguageInstruction(language);
    }

    private BudgetDecision TryConsumeDeepInsightsBudget(DeepInsightsRequest request)
    {
        var settings = _settingsMonitor.CurrentValue.DeepInsights;
        var maxRequestsPerMinute = Math.Max(1, settings.MaxRequestsPerMinute);
        var cooldownSeconds = Math.Max(0, settings.CooldownSeconds);
        var requester = string.IsNullOrWhiteSpace(request.RequesterKey) ? "anonymous" : request.RequesterKey.Trim().ToLowerInvariant();
        var key = $"{request.DatasetId:N}:{requester}";
        var now = DateTime.UtcNow;

        var state = DeepInsightsBudget.GetOrAdd(key, _ => new BudgetState());
        lock (state.Sync)
        {
            while (state.Requests.Count > 0 && (now - state.Requests.Peek()).TotalSeconds > 60)
            {
                state.Requests.Dequeue();
            }

            if (cooldownSeconds > 0 && state.LastRequestUtc != default && (now - state.LastRequestUtc).TotalSeconds < cooldownSeconds)
            {
                return BudgetDecision.Reject($"Deep insights cooldown active. Wait {cooldownSeconds}s before retrying.");
            }

            if (state.Requests.Count >= maxRequestsPerMinute)
            {
                return BudgetDecision.Reject("Deep insights request budget exceeded for this dataset. Please retry in one minute.");
            }

            state.Requests.Enqueue(now);
            state.LastRequestUtc = now;
            return BudgetDecision.Allow();
        }
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
Use only numbers available in the provided context.
If a number is missing, explicitly say it is unknown.
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
Use only numeric facts from the context. Do not invent values.
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
- If scenarioMeta is present, mention scenario impact only using provided delta fields.
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
- If scenarioMeta is present, explain baseline vs scenario deltas without inventing values.
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
If information is missing from context, keep fields null instead of guessing.
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

    private static string NormalizeRiskLevel(string? value, string fallback)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "low" => "low",
            "high" => "high",
            _ => fallback
        };
    }

    private static string NormalizeProjectionMethod(string? method)
    {
        return method?.Trim().ToLowerInvariant() switch
        {
            "naive" => "naive",
            "movingaverage" => "movingAverage",
            "linearregression" => "linearRegression",
            _ => "naive"
        };
    }

    private static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        var chars = value
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }

    private static readonly ConcurrentDictionary<string, BudgetState> DeepInsightsBudget = new(StringComparer.OrdinalIgnoreCase);

    private sealed class BudgetState
    {
        public object Sync { get; } = new();
        public Queue<DateTime> Requests { get; } = new();
        public DateTime LastRequestUtc { get; set; }
    }

    private readonly record struct BudgetDecision(bool IsAllowed, string? Reason)
    {
        public static BudgetDecision Allow() => new(true, null);
        public static BudgetDecision Reject(string reason) => new(false, reason);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
