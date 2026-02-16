using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class LocalHttpLLMClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly ILogger<LocalHttpLLMClient> _logger;

    public LocalHttpLLMClient(
        HttpClient httpClient,
        IOptionsMonitor<LLMSettings> settingsMonitor,
        ILogger<LocalHttpLLMClient> logger)
    {
        _httpClient = httpClient;
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    public Task<Result<LLMResponse>> GenerateTextAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return GenerateInternalAsync(request, LLMResponseFormat.Text, cancellationToken);
    }

    public Task<Result<LLMResponse>> GenerateJsonAsync(LLMRequest request, CancellationToken cancellationToken = default)
    {
        return GenerateInternalAsync(request, LLMResponseFormat.Json, cancellationToken);
    }

    private async Task<Result<LLMResponse>> GenerateInternalAsync(
        LLMRequest request,
        LLMResponseFormat responseFormat,
        CancellationToken cancellationToken)
    {
        var settings = _settingsMonitor.CurrentValue;
        var sw = Stopwatch.StartNew();

        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds));
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var endpoint = BuildEndpoint(settings.LocalHttp.BaseUrl);
            var payload = BuildPayload(request, settings, responseFormat);

            _logger.LogInformation(
                "Calling local LLM endpoint {Endpoint} with provider={Provider}, model={Model}, systemPromptLength={SystemPromptLength}, userPromptLength={UserPromptLength}, contextKeys={ContextCount}",
                endpoint,
                settings.Provider,
                settings.LocalHttp.Model,
                request.SystemPrompt.Length,
                request.UserPrompt.Length,
                request.ContextObjects.Count);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var responseContent = await httpResponse.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Local LLM request failed with status={StatusCode}, bodyLength={BodyLength}",
                    (int)httpResponse.StatusCode,
                    responseContent.Length);

                return Result.Failure<LLMResponse>($"Local LLM request failed: {(int)httpResponse.StatusCode}.");
            }

            var parsed = ParseResponse(responseContent);
            if (!parsed.IsSuccess)
            {
                return Result.Failure<LLMResponse>(parsed.Errors);
            }

            sw.Stop();
            var llmResponse = parsed.Data!;
            llmResponse.Provider = LLMProvider.LocalHttp;
            llmResponse.ModelId = string.IsNullOrWhiteSpace(llmResponse.ModelId) ? settings.LocalHttp.Model : llmResponse.ModelId;
            llmResponse.DurationMs = sw.ElapsedMilliseconds;

            if (responseFormat == LLMResponseFormat.Json)
            {
                if (!TryNormalizeJson(llmResponse.Text, out var normalizedJson))
                {
                    return Result.Failure<LLMResponse>("Local LLM returned non-JSON content for JSON response.");
                }

                llmResponse.Json = normalizedJson;
            }

            return Result.Success(llmResponse);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("Local LLM request timed out after {DurationMs}ms.", sw.ElapsedMilliseconds);
            return Result.Failure<LLMResponse>("Local LLM request timed out.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Local LLM request failed unexpectedly.");
            return Result.Failure<LLMResponse>("Local LLM request failed unexpectedly.");
        }
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:11434"
            : baseUrl.TrimEnd('/');
        return $"{normalizedBaseUrl}/api/generate";
    }

    private static string BuildPayload(LLMRequest request, LLMSettings settings, LLMResponseFormat format)
    {
        var contextJson = request.ContextObjects.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(request.ContextObjects, SerializerOptions);
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(request.UserPrompt);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Context:");
        promptBuilder.Append(contextJson);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.LocalHttp.Model,
            ["prompt"] = promptBuilder.ToString(),
            ["system"] = request.SystemPrompt,
            ["stream"] = false,
            ["options"] = new Dictionary<string, object?>
            {
                ["temperature"] = request.Temperature ?? settings.Temperature,
                ["num_predict"] = request.MaxTokens ?? settings.MaxTokens
            }
        };

        if (format == LLMResponseFormat.Json)
        {
            payload["format"] = "json";
        }

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static Result<LLMResponse> ParseResponse(string responseContent)
    {
        try
        {
            using var document = JsonDocument.Parse(responseContent);
            var root = document.RootElement;

            var text = root.TryGetProperty("response", out var responseProp)
                ? responseProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(text) &&
                root.TryGetProperty("message", out var messageProp) &&
                messageProp.ValueKind == JsonValueKind.Object &&
                messageProp.TryGetProperty("content", out var contentProp))
            {
                text = contentProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<LLMResponse>("Local LLM response does not include text content.");
            }

            var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? string.Empty : string.Empty;

            int promptTokens = 0;
            int completionTokens = 0;
            if (root.TryGetProperty("prompt_eval_count", out var promptCountProp) && promptCountProp.TryGetInt32(out var parsedPromptCount))
            {
                promptTokens = parsedPromptCount;
            }

            if (root.TryGetProperty("eval_count", out var completionCountProp) && completionCountProp.TryGetInt32(out var parsedCompletionCount))
            {
                completionTokens = parsedCompletionCount;
            }

            var usage = new LLMTokenUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            };

            return Result.Success(new LLMResponse
            {
                Text = text.Trim(),
                ModelId = model,
                TokenUsage = usage
            });
        }
        catch (JsonException)
        {
            return Result.Failure<LLMResponse>("Invalid JSON returned from local LLM endpoint.");
        }
    }

    private static bool TryNormalizeJson(string? candidate, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Trim('`').Trim();
            if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[4..].Trim();
            }
        }

        try
        {
            using var parsed = JsonDocument.Parse(trimmed);
            json = JsonSerializer.Serialize(parsed.RootElement, SerializerOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
