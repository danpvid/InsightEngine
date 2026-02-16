namespace InsightEngine.Domain.Models;

public class InsightSummary
{
    public string Headline { get; set; } = string.Empty;
    public List<string> BulletPoints { get; set; } = new();
    public InsightSignals Signals { get; set; } = new();
    public double Confidence { get; set; }
}

public class InsightSignals
{
    public TrendSignal Trend { get; set; } = TrendSignal.Flat;
    public VolatilitySignal Volatility { get; set; } = VolatilitySignal.Medium;
    public OutlierSignal Outliers { get; set; } = OutlierSignal.None;
    public SeasonalitySignal Seasonality { get; set; } = SeasonalitySignal.None;
}

public enum TrendSignal
{
    Down,
    Flat,
    Up
}

public enum VolatilitySignal
{
    Low,
    Medium,
    High
}

public enum OutlierSignal
{
    None,
    Few,
    Many
}

public enum SeasonalitySignal
{
    None,
    Weak,
    Strong
}
