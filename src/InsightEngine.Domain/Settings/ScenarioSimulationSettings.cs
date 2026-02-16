namespace InsightEngine.Domain.Settings;

public class ScenarioSimulationSettings
{
    public int MaxOperations { get; set; } = 3;
    public int MaxFilters { get; set; } = 3;
    public int MaxRowsReturned { get; set; } = 200;
}
