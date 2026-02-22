using System.Text;
using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.Insights;

namespace InsightEngine.Domain.Services;

public static class InsightPromptBuilder
{
    public static InsightPromptSpec BuildInsightAskPrompt(
        SemanticInsightPack pack,
        string userQuestion,
        string language,
        string? filterSummary = null)
    {
        var columns = pack.PackV2?.SchemaContext.Columns.Select(column => column.Name).ToList() ?? new List<string>();
        var anchors = pack.PackV2?.EvidenceIndex.Select(anchor => anchor.Id).ToList() ?? new List<string>();

        var systemPrompt = new StringBuilder();
        systemPrompt.AppendLine("You are a senior analytics copilot that answers ONLY with provided Insight Pack evidence.");
        systemPrompt.AppendLine("Never claim causality. Use terms like 'associated with', 'likely driver', or 'correlates with'.");
        systemPrompt.AppendLine("Never assume raw rows access and never fabricate numbers.");
        systemPrompt.AppendLine("Use ONLY columns listed in schemaContext.columns.");
        systemPrompt.AppendLine("For percentageScaleHint=Unknown, add a caveat asking user confirmation before strong conclusions.");
        systemPrompt.AppendLine("Any numeric claim must include at least one evidence anchor.");
        systemPrompt.AppendLine("Return strict JSON only, no markdown.");
        systemPrompt.AppendLine();
        if (columns.Count > 0)
        {
            systemPrompt.AppendLine($"Allowed columns: {string.Join(", ", columns)}");
        }

        if (anchors.Count > 0)
        {
            systemPrompt.AppendLine($"Allowed evidence anchors: {string.Join(", ", anchors)}");
        }

        systemPrompt.AppendLine(BuildOutputLanguageInstruction(language));

        var userPrompt = new StringBuilder();
        userPrompt.AppendLine("User question:");
        userPrompt.AppendLine(userQuestion.Trim());
        if (!string.IsNullOrWhiteSpace(filterSummary))
        {
            userPrompt.AppendLine();
            userPrompt.AppendLine($"Active filters/timeframe: {filterSummary}");
        }

        userPrompt.AppendLine();
        userPrompt.AppendLine("Return this JSON schema exactly:");
        userPrompt.AppendLine(InsightAskJsonSchema);

        return new InsightPromptSpec
        {
            SystemPrompt = systemPrompt.ToString().Trim(),
            UserPrompt = userPrompt.ToString().Trim(),
            JsonSchema = InsightAskJsonSchema
        };
    }

    public static string BuildRepairPrompt(string invalidPayload)
    {
        var truncated = invalidPayload.Length <= 6000 ? invalidPayload : invalidPayload[..6000];
        return $"""
The previous response is invalid JSON or does not follow schema.
Repair it into valid JSON only, preserving original meaning and evidence anchors.
Do not add markdown fences.
Schema:
{InsightAskJsonSchema}
Invalid response:
{truncated}
""";
    }

    private static string BuildOutputLanguageInstruction(string language)
    {
        return language.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            ? "Output language must be Portuguese (pt-BR)."
            : "Output language must be English.";
    }

    public const string InsightAskJsonSchema = """
{
  "executiveSummary": ["string", "string", "string"],
  "keyFindings": [
    {
      "title": "string",
      "explanation": "string",
      "evidence": ["T1", "D2"],
      "confidence": "High|Medium|Low"
    }
  ],
  "topDrivers": {
    "negative": [
      {
        "name": "string",
        "why": "string",
        "evidence": ["D1"],
        "confidence": "High|Medium|Low"
      }
    ],
    "positive": [
      {
        "name": "string",
        "why": "string",
        "evidence": ["D2"],
        "confidence": "High|Medium|Low"
      }
    ]
  },
  "offenders": [
    {
      "name": "string",
      "impact": "string",
      "evidence": ["O1"],
      "confidence": "High|Medium|Low"
    }
  ],
  "recommendations": [
    {
      "action": "string",
      "expectedImpact": "IncreaseTarget|DecreaseTarget",
      "why": "string",
      "evidence": ["O1", "D1"],
      "risk": "string"
    }
  ],
  "caveats": ["string"],
  "followUpQuestions": ["string", "string", "string"]
}
""";
}

public class InsightPromptSpec
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public string JsonSchema { get; set; } = string.Empty;
}
