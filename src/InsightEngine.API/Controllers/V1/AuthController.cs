using InsightEngine.API.CQRS.Auth;
using InsightEngine.API.Models;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : BaseController
{
    public AuthController(IDomainNotificationHandler notificationHandler, IMediator mediator)
        : base(notificationHandler, mediator)
    {
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var result = await _mediator.Send(new RegisterCommand(request.Email, request.Password, request.DisplayName));
        return ResponseResult(result, StatusCodes.Status201Created);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password));
        return ResponseResult(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await _mediator.Send(new RefreshTokenCommand(request.AccessToken, request.RefreshToken));
        return ResponseResult(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var result = await _mediator.Send(new LogoutCommand(request.RefreshToken));
        return ResponseResult(result);
    }
}
