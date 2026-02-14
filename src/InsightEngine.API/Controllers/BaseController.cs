using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    private readonly IDomainNotificationHandler _notificationHandler;
    private readonly IMediator _mediator;

    protected BaseController(IDomainNotificationHandler notificationHandler, IMediator mediator)
    {
        _notificationHandler = notificationHandler;
        _mediator = mediator;
    }

    protected bool IsValidOperation()
    {
        return !_notificationHandler.HasNotifications();
    }

    protected IActionResult ResponseCommand(object? result = null)
    {
        if (IsValidOperation())
        {
            return Ok(new
            {
                success = true,
                data = result
            });
        }

        var notifications = _notificationHandler.GetNotifications();
        return BadRequest(new
        {
            success = false,
            errors = notifications.Select(n => new { n.Key, n.Value })
        });
    }

    protected IActionResult ResponseQuery(object? result = null)
    {
        if (result == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Recurso não encontrado"
            });
        }

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    protected async Task<IActionResult> SendCommand<TCommand>(TCommand command) where TCommand : class, IRequest<bool>
    {
        var result = await _mediator.Send(command);
        return ResponseCommand(result);
    }

    protected async Task<IActionResult> SendQuery<TQuery, TResponse>(TQuery query) where TQuery : IRequest<TResponse>
    {
        var result = await _mediator.Send(query);
        return ResponseQuery(result);
    }
}
