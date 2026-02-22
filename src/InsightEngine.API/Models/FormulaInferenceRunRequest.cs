namespace InsightEngine.API.Models;

public class FormulaInferenceRunRequest
{
    public string TargetColumn { get; set; } = string.Empty;
    public string Mode { get; set; } = "Auto";
    public string? ManualExpression { get; set; }
    public FormulaInferenceRunOptions? Options { get; set; }
}

public class FormulaInferenceRunOptions
{
    public int? MaxColumns { get; set; }
    public int? MaxDepth { get; set; }
    public double? EpsilonAbs { get; set; }
    public bool IncludePercentageColumns { get; set; } = false;
}
