using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IChartFilterParser
{
    Result<List<ChartFilter>> Parse(string[]? filters);
}
