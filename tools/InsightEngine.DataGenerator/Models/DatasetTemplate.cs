namespace InsightEngine.DataGenerator.Models;

public class DatasetTemplate
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int RowCount { get; set; } = 5000;
    public List<ColumnDefinition> Columns { get; set; } = new();
}
