using InsightEngine.Domain.Enums;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Services;

public static class EChartsOptionTemplateFactory
{
    public static Dictionary<string, object> Create(ChartRecommendation recommendation)
    {
        return recommendation.Chart.Type switch
        {
            ChartType.Line => CreateLineTemplate(),
            ChartType.Bar => CreateBarTemplate(),
            ChartType.Scatter => CreateScatterTemplate(),
            ChartType.Histogram => CreateHistogramTemplate(),
            _ => new Dictionary<string, object>()
        };
    }

    private static Dictionary<string, object> CreateLineTemplate()
    {
        return new Dictionary<string, object>
        {
            ["tooltip"] = new { trigger = "axis" },
            ["xAxis"] = new { type = "time" },
            ["yAxis"] = new { type = "value" },
            ["series"] = new[]
            {
                new
                {
                    type = "line",
                    smooth = true,
                    data = Array.Empty<object>()
                }
            }
        };
    }

    private static Dictionary<string, object> CreateBarTemplate()
    {
        return new Dictionary<string, object>
        {
            ["tooltip"] = new { trigger = "axis" },
            ["xAxis"] = new
            {
                type = "category",
                data = Array.Empty<string>()
            },
            ["yAxis"] = new { type = "value" },
            ["series"] = new[]
            {
                new
                {
                    type = "bar",
                    data = Array.Empty<object>()
                }
            }
        };
    }

    private static Dictionary<string, object> CreateScatterTemplate()
    {
        return new Dictionary<string, object>
        {
            ["tooltip"] = new { trigger = "item" },
            ["xAxis"] = new { type = "value" },
            ["yAxis"] = new { type = "value" },
            ["series"] = new[]
            {
                new
                {
                    type = "scatter",
                    data = Array.Empty<object>()
                }
            }
        };
    }

    private static Dictionary<string, object> CreateHistogramTemplate()
    {
        // Histogram é representado como Bar com bins
        return new Dictionary<string, object>
        {
            ["tooltip"] = new { trigger = "axis" },
            ["xAxis"] = new
            {
                type = "category",
                data = Array.Empty<string>()
            },
            ["yAxis"] = new { type = "value" },
            ["series"] = new[]
            {
                new
                {
                    type = "bar",
                    data = Array.Empty<object>()
                }
            }
        };
    }
}
