using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class LocalHttpLLMClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;
    private readonly ILogger<LocalHttpLLMClient> _logger;

    public LocalHttpLLMClient(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptionsMonitor<LLMSettings> settingsMonitor,
        ILogger<LocalHttpLLMClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
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

            var baseUrl = NormalizeBaseUrl(settings.LocalHttp.BaseUrl);
            var endpoint = BuildEndpoint(baseUrl);
            var resolvedModel = await ResolveModelAsync(baseUrl, settings.LocalHttp, timeoutCts.Token);
            var payload = BuildPayload(request, settings, responseFormat, resolvedModel);

            _logger.LogInformation(
                "Calling local LLM endpoint {Endpoint} with provider={Provider}, model={Model}, systemPromptLength={SystemPromptLength}, userPromptLength={UserPromptLength}, contextKeys={ContextCount}",
                endpoint,
                settings.Provider,
                resolvedModel,
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
                var serverMessage = TryReadServerErrorMessage(responseContent);
                _logger.LogWarning(
                    "Local LLM request failed with status={StatusCode}, model={Model}, serverMessage={ServerMessage}, bodyLength={BodyLength}",
                    (int)httpResponse.StatusCode,
                    resolvedModel,
                    serverMessage,
                    responseContent.Length);

                return Result.Failure<LLMResponse>(
                    BuildFailureMessage((int)httpResponse.StatusCode, baseUrl, resolvedModel, serverMessage));
            }

            var parsed = ParseResponse(responseContent);
            if (!parsed.IsSuccess)
            {
                return Result.Failure<LLMResponse>(parsed.Errors);
            }

            sw.Stop();
            var llmResponse = parsed.Data!;
            llmResponse.Provider = LLMProvider.LocalHttp;
            llmResponse.ModelId = string.IsNullOrWhiteSpace(llmResponse.ModelId) ? resolvedModel : llmResponse.ModelId;
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
        catch (HttpRequestException ex)
        {
            sw.Stop();
            var baseUrl = NormalizeBaseUrl(settings.LocalHttp.BaseUrl);
            _logger.LogWarning(ex, "Local LLM endpoint is unreachable at {BaseUrl}.", baseUrl);
            return Result.Failure<LLMResponse>(
                $"Local LLM endpoint is unreachable at {baseUrl}. Make sure Ollama is running.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Local LLM request failed unexpectedly.");
            return Result.Failure<LLMResponse>("Local LLM request failed unexpectedly.");
        }
    }

    private static string BuildEndpoint(string normalizedBaseUrl)
    {
        return $"{normalizedBaseUrl}/api/generate";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl)
            ? "http://localhost:11434"
            : baseUrl.TrimEnd('/');
    }

    private static string BuildPayload(LLMRequest request, LLMSettings settings, LLMResponseFormat format, string model)
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
            ["model"] = model,
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

        if (TryParseAndSerializeJson(trimmed, out json))
        {
            return true;
        }

        if (TryExtractFirstJsonObject(trimmed, out var extracted) &&
            TryParseAndSerializeJson(extracted, out json))
        {
            return true;
        }

        return false;
    }

    private async Task<string> ResolveModelAsync(
        string baseUrl,
        LocalHttpSettings settings,
        CancellationToken cancellationToken)
    {
        var configuredModel = string.IsNullOrWhiteSpace(settings.Model)
            ? "llama3"
            : settings.Model.Trim();

        if (!settings.AutoSelectInstalledModel)
        {
            return configuredModel;
        }

        var installedModels = await GetInstalledModelsAsync(baseUrl, cancellationToken);
        if (installedModels.Count == 0)
        {
            return configuredModel;
        }

        var configuredMatch = FindModelMatch(installedModels, configuredModel);
        if (configuredMatch != null)
        {
            return configuredMatch;
        }

        foreach (var fallback in settings.FallbackModels.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            var match = FindModelMatch(installedModels, fallback);
            if (match != null)
            {
                _logger.LogWarning(
                    "Configured LLM model {ConfiguredModel} is not installed. Falling back to installed model {ResolvedModel}.",
                    configuredModel,
                    match);
                return match;
            }
        }

        var preferred = installedModels.FirstOrDefault(model =>
            model.Contains("llama", StringComparison.OrdinalIgnoreCase))
            ?? installedModels[0];

        _logger.LogWarning(
            "Configured LLM model {ConfiguredModel} is not installed. Falling back to available model {ResolvedModel}.",
            configuredModel,
            preferred);

        return preferred;
    }

    private async Task<IReadOnlyList<string>> GetInstalledModelsAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var cacheKey = $"llm:installed-models:{baseUrl}";
        if (_cache.TryGetValue<IReadOnlyList<string>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        try
        {
            using var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(payload);

            if (!json.RootElement.TryGetProperty("models", out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var models = new List<string>();
            foreach (var item in modelsElement.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nameElement))
                {
                    var modelName = nameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(modelName))
                    {
                        models.Add(modelName.Trim());
                    }
                }
            }

            if (models.Count > 0)
            {
                _cache.Set(cacheKey, models, TimeSpan.FromMinutes(2));
            }

            return models;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? FindModelMatch(IReadOnlyList<string> installedModels, string requestedModel)
    {
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            return null;
        }

        var normalized = requestedModel.Trim();
        var exact = installedModels.FirstOrDefault(model =>
            string.Equals(model, normalized, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        return installedModels.FirstOrDefault(model =>
            model.StartsWith($"{normalized}:", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseAndSerializeJson(string candidate, out string json)
    {
        json = string.Empty;
        try
        {
            using var parsed = JsonDocument.Parse(candidate);
            json = JsonSerializer.Serialize(parsed.RootElement, SerializerOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractFirstJsonObject(string text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var startIndex = text.IndexOf('{');
        if (startIndex < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = text[startIndex..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private static string TryReadServerErrorMessage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return "unknown";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errorElement))
            {
                return TruncateError(errorElement.GetString());
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                return TruncateError(messageElement.GetString());
            }
        }
        catch
        {
            // Keep raw fallback for non-json errors.
        }

        return TruncateError(responseContent);
    }

    private static string BuildFailureMessage(int statusCode, string baseUrl, string model, string serverMessage)
    {
        if (statusCode == 404 || statusCode == 400)
        {
            return $"Local LLM request failed ({statusCode}) for model '{model}'. " +
                   $"Server message: {serverMessage}. " +
                   $"Check your local provider at {baseUrl} and confirm the model is available (e.g. `ollama pull {model}`).";
        }

        return $"Local LLM request failed ({statusCode}). Server message: {serverMessage}.";
    }

    private static string TruncateError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var normalized = raw.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 220 ? normalized : normalized[..220];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
