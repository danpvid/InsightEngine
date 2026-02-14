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
        
        // Determinar status code baseado no tipo de erro das notificações
        var statusCode = DetermineStatusCodeFromNotifications(notifications);
        
        var errors = new Dictionary<string, List<string>>();
        
        foreach (var notification in notifications)
        {
            if (!errors.ContainsKey(notification.Key))
                errors[notification.Key] = new List<string>();
            
            errors[notification.Key].Add(notification.Value);
        }

        var errorResponse = ApiErrorResponse.FromValidationErrors(errors, traceId);
        errorResponse.Code = MapErrorTypeToCode(notifications.FirstOrDefault()?.Type ?? ErrorType.Validation);
        
        return StatusCode(statusCode, errorResponse);
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
            firstError.Contains("required") || firstError.Contains("obrigatório") ||
            firstError.Contains("must") || firstError.Contains("deve") ||
            firstError.Contains("pattern") || firstError.Contains("formato"))
            return 400;

        // Erro genérico/interno
        return 500;
    }

    /// <summary>
    /// Determina o status code HTTP baseado no tipo de erro das notificações
    /// </summary>
    private int DetermineStatusCodeFromNotifications(IReadOnlyCollection<DomainNotification> notifications)
    {
        if (notifications == null || notifications.Count == 0)
            return 500;

        // Usar o tipo da primeira notificação (priorizar NotFound > Conflict > Validation)
        var priorityOrder = new[] 
        { 
            ErrorType.NotFound, 
            ErrorType.Conflict, 
            ErrorType.UnprocessableEntity,
            ErrorType.Validation,
            ErrorType.InternalError 
        };

        foreach (var errorType in priorityOrder)
        {
            if (notifications.Any(n => n.Type == errorType))
                return (int)errorType;
        }

        return 500;
    }

    /// <summary>
    /// Mapeia ErrorType para código de erro legível
    /// </summary>
    private string MapErrorTypeToCode(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation => "validation_error",
            ErrorType.NotFound => "not_found",
            ErrorType.Conflict => "conflict",
            ErrorType.UnprocessableEntity => "unprocessable_entity",
            ErrorType.InternalError => "internal_error",
            _ => "internal_error"
        };
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
