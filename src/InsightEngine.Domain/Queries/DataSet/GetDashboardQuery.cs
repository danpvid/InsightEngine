using InsightEngine.Domain.Core;
using InsightEngine.Domain.Models.Dashboard;

namespace InsightEngine.Domain.Queries.DataSet;

public class GetDashboardQuery : Query<DashboardViewModel>
{
    public Guid DatasetId { get; }

    public GetDashboardQuery(Guid datasetId)
    {
        DatasetId = datasetId;
    }
}
