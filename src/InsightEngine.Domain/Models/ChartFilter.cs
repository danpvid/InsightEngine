namespace InsightEngine.Domain.Models;

public class ChartFilter
{
    public string Column { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; }
    public List<string> Values { get; set; } = new();
    public FilterLogicalOperator LogicalOperator { get; set; } = FilterLogicalOperator.And;
}

public enum FilterOperator
{
    Eq,
    NotEq,
    Gt,
    Gte,
    Lt,
    Lte,
    In,
    Between,
    Contains
}

public enum FilterLogicalOperator
{
    And,
    Or
}
