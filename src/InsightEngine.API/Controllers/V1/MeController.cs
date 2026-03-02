using InsightEngine.API.CQRS.Auth;
using InsightEngine.API.Models;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public class MeController : BaseController
{
    public MeController(IDomainNotificationHandler notificationHandler, IMediator mediator)
        : base(notificationHandler, mediator)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetMe()
    {
        var result = await _mediator.Send(new GetMeQuery());
        return ResponseResult(result);
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var result = await _mediator.Send(new UpdateMeProfileCommand(request.DisplayName));
        return ResponseResult(result);
    }

    [HttpPut("password")]
    public async Task<IActionResult> UpdatePassword([FromBody] UpdatePasswordRequest request)
    {
        var result = await _mediator.Send(new UpdateMePasswordCommand(request.CurrentPassword, request.NewPassword));
        return ResponseResult(result);
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar([FromBody] UpdateAvatarRequest request)
    {
        var result = await _mediator.Send(new UpdateMeAvatarCommand(request.AvatarUrl));
        return ResponseResult(result);
    }

    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan()
    {
        var result = await _mediator.Send(new GetMePlanQuery());
        return ResponseResult(result);
    }

    [HttpPost("plan/upgrade")]
    public async Task<IActionResult> UpgradePlan([FromBody] UpgradePlanRequest request)
    {
        var result = await _mediator.Send(new UpgradeMePlanCommand(request.TargetPlan));
        return ResponseResult(result);
    }
}
