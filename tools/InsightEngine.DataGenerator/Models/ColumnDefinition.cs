namespace InsightEngine.DataGenerator.Models;

public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public ColumnType Type { get; set; }
    public double NullRate { get; set; } = 0.0;
    public int? Cardinality { get; set; }
    public List<string>? PossibleValues { get; set; }
    public (decimal Min, decimal Max)? NumberRange { get; set; }
    public (DateTime Min, DateTime Max)? DateRange { get; set; }
}

public enum ColumnType
{
    Number,
    Date,
    Boolean,
    Category,
    String
}
