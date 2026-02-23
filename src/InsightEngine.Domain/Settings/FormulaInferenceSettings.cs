namespace InsightEngine.Domain.Settings;

public class FormulaInferenceSettings
{
    public const string SectionName = "FormulaInference";

    public bool EnabledDefault { get; set; } = false;
    public int MaxColumns { get; set; } = 10;
    public int MaxDepth { get; set; } = 5;
    public int MaxCandidatesReturned { get; set; } = 5;
    public int SearchBudgetMs { get; set; } = 10000;
    public int InitialSampleRows { get; set; } = 300;
    public int ValidationSampleRows { get; set; } = 2000;
    public double EpsilonAbs { get; set; } = 1e-6;
    public double EpsilonAbsRelaxed { get; set; } = 1e-3;
    public double DivisionZeroEpsilon { get; set; } = 1e-12;
    public bool AllowConstants { get; set; } = false;
    public bool AllowColumnReuse { get; set; } = false;
    public int BeamWidth { get; set; } = 200;
}
