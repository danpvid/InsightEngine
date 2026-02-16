using System.Text.Json;
using System.Text.Json.Nodes;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Settings;
using Microsoft.Extensions.Options;

namespace InsightEngine.Infra.Data.Services;

public class LLMRedactionService : ILLMRedactionService
{
    private readonly IOptionsMonitor<LLMSettings> _settingsMonitor;

    public LLMRedactionService(IOptionsMonitor<LLMSettings> settingsMonitor)
    {
        _settingsMonitor = settingsMonitor;
    }

    public Dictionary<string, object?> RedactContext(Dictionary<string, object?> context)
    {
        var settings = _settingsMonitor.CurrentValue.Redaction;
        if (!settings.Enabled || context.Count == 0)
        {
            return new Dictionary<string, object?>(context, StringComparer.OrdinalIgnoreCase);
        }

        var patterns = settings.ColumnNamePatterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (patterns.Count == 0)
        {
            return new Dictionary<string, object?>(context, StringComparer.OrdinalIgnoreCase);
        }

        var rootNode = JsonSerializer.SerializeToNode(context);
        if (rootNode == null)
        {
            return new Dictionary<string, object?>(context, StringComparer.OrdinalIgnoreCase);
        }

        RedactNode(rootNode, patterns);
        var json = rootNode.ToJsonString();

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static void RedactNode(JsonNode node, IReadOnlyCollection<string> patterns)
    {
        if (node is JsonObject jsonObject)
        {
            var keys = jsonObject.Select(property => property.Key).ToList();
            foreach (var key in keys)
            {
                if (IsSensitive(key, patterns))
                {
                    jsonObject.Remove(key);
                    continue;
                }

                if (jsonObject[key] is JsonNode childNode)
                {
                    RedactNode(childNode, patterns);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            for (var index = jsonArray.Count - 1; index >= 0; index--)
            {
                var child = jsonArray[index];
                if (child == null)
                {
                    continue;
                }

                if (IsSensitiveColumnDescriptor(child, patterns))
                {
                    jsonArray.RemoveAt(index);
                    continue;
                }

                RedactNode(child, patterns);
            }
        }
    }

    private static bool IsSensitiveColumnDescriptor(JsonNode node, IReadOnlyCollection<string> patterns)
    {
        if (node is not JsonObject jsonObject)
        {
            return false;
        }

        foreach (var field in new[] { "name", "column", "field", "key" })
        {
            if (jsonObject.TryGetPropertyValue(field, out var propertyNode) &&
                propertyNode is JsonValue propertyValue &&
                propertyValue.TryGetValue<string>(out var propertyText) &&
                IsSensitive(propertyText, patterns))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSensitive(string? text, IReadOnlyCollection<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
