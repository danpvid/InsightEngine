using InsightEngine.Domain.Models;
using InsightEngine.Domain.Models.MetadataIndex;
using InsightEngine.Domain.ValueObjects;

namespace InsightEngine.Domain.Interfaces;

public interface IRecommendationEngineV2
{
    List<ChartRecommendation> Generate(DatasetProfile profile, DatasetIndex? index);
}
