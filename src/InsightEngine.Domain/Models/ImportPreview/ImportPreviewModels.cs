using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.ImportPreview;

public class ImportPreviewRequest
{
    public int SampleSize { get; set; } = 200;
}

public class ImportPreviewResponse
{
    public string TempUploadId { get; set; } = string.Empty;
    public int SampleSize { get; set; }
    public List<ImportPreviewColumn> Columns { get; set; } = new();
    public List<Dictionary<string, string>> SampleRows { get; set; } = new();
    public List<string> SuggestedTargetCandidates { get; set; } = new();
    public List<string> SuggestedIgnoredCandidates { get; set; } = new();
    public List<string> SuggestedUniqueKeyCandidates { get; set; } = new();
}

public class ImportPreviewColumn
{
    public string Name { get; set; } = string.Empty;
    public InferredType InferredType { get; set; }
    public double Confidence { get; set; }
    public List<string> Reasons { get; set; } = new();
    public ImportPreviewHints Hints { get; set; } = new();
}

public class ImportPreviewHints
{
    public bool HasPercentSign { get; set; }
    public bool HasCurrencySymbol { get; set; }
    public bool MostlyZeroToOne { get; set; }
    public bool MostlyZeroToHundred { get; set; }
    public bool MostlyInteger { get; set; }
    public bool ConsistentTwoDecimalPlaces { get; set; }
    public PercentageScaleHint? PercentageScaleHint { get; set; }
    public string CurrencyCode { get; set; } = "BRL";
}
