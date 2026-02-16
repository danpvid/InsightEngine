using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Helpers;

public static class LLMCacheKeyHelper
{
    public static string Build(LLMRequest request, LLMProvider provider, string model)
    {
        var datasetId = request.DatasetId?.ToString() ?? "none";
        var recommendationId = string.IsNullOrWhiteSpace(request.RecommendationId) ? "none" : request.RecommendationId.Trim();
        var queryHash = string.IsNullOrWhiteSpace(request.QueryHash) ? "none" : request.QueryHash.Trim();
        var featureKind = string.IsNullOrWhiteSpace(request.FeatureKind) ? "generic" : request.FeatureKind.Trim().ToLowerInvariant();
        var promptVersion = string.IsNullOrWhiteSpace(request.PromptVersion) ? "v1" : request.PromptVersion.Trim();
        var normalizedModel = string.IsNullOrWhiteSpace(model) ? "default" : model.Trim();
        var promptHash = ComputePromptHash(request);

        return string.Join(
            "|",
            datasetId,
            recommendationId,
            queryHash,
            promptVersion,
            provider.ToString(),
            normalizedModel,
            featureKind,
            promptHash);
    }

    private static string ComputePromptHash(LLMRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.SystemPrompt,
            request.UserPrompt,
            request.ContextObjects
        });

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
