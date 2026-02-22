using InsightEngine.Domain.Enums;

namespace InsightEngine.Domain.Models.Formulas;

public class FormulaExpression
{
    public string ExpressionText { get; set; } = string.Empty;
    public string TargetColumn { get; set; } = string.Empty;
    public string[] UsedColumns { get; set; } = [];
    public int Depth { get; set; }
    public FormulaOperator[] OperatorsUsed { get; set; } = [];
    public double EpsilonMaxAbsError { get; set; }
    public int SampleRowsTested { get; set; }
    public int RowsFailed { get; set; }
    public FormulaConfidence Confidence { get; set; } = FormulaConfidence.Low;
    public string Notes { get; set; } = string.Empty;
}
