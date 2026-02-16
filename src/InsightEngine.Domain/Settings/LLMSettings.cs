using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Settings;

public class LLMSettings
{
    public const string SectionName = "LLM";

    public LLMProvider Provider { get; set; } = LLMProvider.None;
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxTokens { get; set; } = 512;
    public int MaxContextBytes { get; set; } = 24_000;
    public int AskMaxQuestionChars { get; set; } = 600;
    public double Temperature { get; set; } = 0.2;
    public bool EnableCaching { get; set; } = true;
    public LocalHttpSettings LocalHttp { get; set; } = new();
    public RedactionSettings Redaction { get; set; } = new();
    public DeepInsightsSettings DeepInsights { get; set; } = new();
}

public class LocalHttpSettings
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
}

public class RedactionSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> ColumnNamePatterns { get; set; } =
    [
        "email",
        "mail",
        "phone",
        "mobile",
        "cpf",
        "ssn",
        "document",
        "address"
    ];
}

public class DeepInsightsSettings
{
    public string EvidenceVersion { get; set; } = "v1";
    public int MaxEvidenceSeriesPoints { get; set; } = 400;
    public int ForecastDefaultHorizon { get; set; } = 30;
    public int ForecastMaxHorizon { get; set; } = 90;
    public int ForecastMovingAverageWindow { get; set; } = 5;
    public int MaxBreakdownSegments { get; set; } = 10;
    public int MaxRequestsPerMinute { get; set; } = 12;
    public int CooldownSeconds { get; set; } = 4;
}
