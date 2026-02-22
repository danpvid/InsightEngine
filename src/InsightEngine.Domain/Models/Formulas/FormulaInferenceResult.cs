using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.Formulas;

public class FormulaInferenceResult
{
    public FormulaInferenceStatus Status { get; set; } = FormulaInferenceStatus.NotRun;
    public DateTimeOffset GeneratedAt { get; set; }
    public string TargetColumn { get; set; } = string.Empty;
    public List<FormulaExpression> Candidates { get; set; } = new();
    public string[] NumericCandidateColumns { get; set; } = [];
    public Dictionary<string, object?> Meta { get; set; } = new();
    public string[] Warnings { get; set; } = [];
}
