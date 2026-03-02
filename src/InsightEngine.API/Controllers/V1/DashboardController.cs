using InsightEngine.API.Models;
using InsightEngine.Domain.Core.Notifications;
using InsightEngine.Domain.Queries.DataSet;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
public class DashboardController : BaseController
{
    public DashboardController(IDomainNotificationHandler notificationHandler, IMediator mediator)
        : base(notificationHandler, mediator)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] GetDashboardRequest request)
    {
        var result = await _mediator.Send(new GetDashboardQuery(request.DatasetId));
        return ResponseResult(result);
    }
}
