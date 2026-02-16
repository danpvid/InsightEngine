namespace InsightEngine.Domain.Models;

public class ChartFilter
{
    public string Column { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; }
    public List<string> Values { get; set; } = new();
}

public enum FilterOperator
{
    Eq,
    NotEq,
    In,
    Between,
    Contains
}
