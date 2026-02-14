using InsightEngine.API.Models;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace InsightEngine.API.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
public abstract class BaseController : ControllerBase
{
    private readonly IDomainNotificationHandler _notificationHandler;
    protected readonly IMediator _mediator;

    protected BaseController(IDomainNotificationHandler notificationHandler, IMediator mediator)
    {
        _notificationHandler = notificationHandler;
        _mediator = mediator;
    }

    /// <summary>
    /// Obtém o TraceId da requisição atual
    /// </summary>
    protected string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    protected bool IsValidOperation()
    {
        return !_notificationHandler.HasNotifications();
    }

    /// <summary>
    /// Retorna resposta padronizada para comandos usando Result Pattern
    /// </summary>
    protected IActionResult ResponseResult<T>(Result<T> result, int successStatusCode = 200)
    {
        var traceId = GetTraceId();

        if (!result.IsSuccess)
        {
            // Determinar status code baseado no tipo de erro
            var statusCode = DetermineStatusCode(result.Errors);
            
            var errorResponse = ApiErrorResponse.FromList(result.Errors, traceId);
            
            return StatusCode(statusCode, errorResponse);
        }

        var response = new ApiResponse<T>(result.Data!, traceId);
        
        return StatusCode(successStatusCode, response);
    }

    /// <summary>
    /// Retorna resposta padronizada para comandos (legado)
    /// </summary>
    protected IActionResult ResponseCommand(object? result = null)
    {
        var traceId = GetTraceId();

        if (IsValidOperation())
        {
            return Ok(new ApiResponse<object>(result!, traceId));
        }

        var notifications = _notificationHandler.GetNotifications();
        var errors = new Dictionary<string, List<string>>();
        
        foreach (var notification in notifications)
        {
            if (!errors.ContainsKey(notification.Key))
                errors[notification.Key] = new List<string>();
            
            errors[notification.Key].Add(notification.Value);
        }

        return BadRequest(new ApiErrorResponse(errors, traceId));
    }

    /// <summary>
    /// Retorna resposta padronizada para queries (legado)
    /// </summary>
    protected IActionResult ResponseQuery(object? result = null)
    {
        var traceId = GetTraceId();

        if (result == null)
        {
            return NotFound(ApiErrorResponse.FromMessage("Recurso não encontrado", traceId));
        }

        return Ok(new ApiResponse<object>(result, traceId));
    }

    /// <summary>
    /// Determina o status code HTTP baseado nas mensagens de erro
    /// </summary>
    private int DetermineStatusCode(List<string> errors)
    {
        if (errors == null || errors.Count == 0)
            return 500;

        // Verificar se é erro de validação
        var firstError = errors[0].ToLowerInvariant();
        
        if (firstError.Contains("not found") || firstError.Contains("não encontrado"))
            return 404;
        
        if (firstError.Contains("validation") || firstError.Contains("invalid") || 
            firstError.Contains("validação") || firstError.Contains("inválido") ||
            firstError.Contains("required") || firstError.Contains("obrigatório"))
            return 400;

        // Erro genérico/interno
        return 500;
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
