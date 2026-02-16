using System.Text.Json;
using InsightEngine.Domain.Constants;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InsightEngine.Application.Services;

public class AIInsightService : IAIInsightService
{
    private readonly ILLMContextBuilder _contextBuilder;
    private readonly ILLMClient _llmClient;
    private readonly ILogger<AIInsightService> _logger;

    public AIInsightService(
        ILLMContextBuilder contextBuilder,
        ILLMClient llmClient,
        ILogger<AIInsightService> logger)
    {
        _contextBuilder = contextBuilder;
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<Result<AiInsightSummaryResult>> GenerateAiSummaryAsync(
        LLMChartContextRequest request,
        CancellationToken cancellationToken = default)
    {
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
            FeatureKind = "ai-summary",
            PromptVersion = LLMPromptVersion.Value,
            ResponseFormat = LLMResponseFormat.Json,
            SystemPrompt = BuildSystemPrompt(),
            UserPrompt = BuildUserPrompt(),
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
            InsightSummary = BuildFallbackSummary(context.HeuristicSummary),
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

    private static AiInsightSummary BuildFallbackSummary(InsightSummary? heuristic)
    {
        if (heuristic == null)
        {
            return new AiInsightSummary
            {
                Headline = "AI summary unavailable",
                BulletPoints =
                [
                    "There is not enough information to build a reliable AI summary right now."
                ],
                Cautions =
                [
                    "This fallback does not include additional AI interpretation."
                ],
                NextQuestions =
                [
                    "Can you adjust aggregation or filters and try again?"
                ],
                Confidence = 0.2
            };
        }

        var nextQuestions = new List<string>
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
                "AI output was unavailable. Showing heuristic insight summary."
            ],
            NextQuestions = nextQuestions,
            Confidence = Math.Clamp(heuristic.Confidence, 0.0, 1.0)
        };
    }

    private static string BuildSystemPrompt()
    {
        return """
You are a data insight assistant. Return valid JSON only.
Do not include markdown or prose outside JSON.
Keep language business-friendly and concise.
Include limitations and assumptions in cautions when uncertainty exists.
""";
    }

    private static string BuildUserPrompt()
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
""";
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
