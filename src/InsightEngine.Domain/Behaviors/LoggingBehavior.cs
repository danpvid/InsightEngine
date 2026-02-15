using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace InsightEngine.Domain.Behaviors;

/// <summary>
/// Pipeline behavior that logs request and response details
/// Useful for debugging and development environments
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // Log request details (be careful with sensitive data in production)
        _logger.LogDebug(
            "Handling {RequestName} - Request: {Request}",
            requestName,
            SerializeRequest(request));

        var response = await next();

        // Log response summary
        _logger.LogDebug(
            "Handled {RequestName} - Response type: {ResponseType}",
            requestName,
            typeof(TResponse).Name);

        return response;
    }

    private string SerializeRequest(TRequest request)
    {
        try
        {
            // Serialize request to JSON for logging
            // In production, consider filtering sensitive properties
            return JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                WriteIndented = false,
                MaxDepth = 3 // Limit depth to avoid circular references
            });
        }
        catch
        {
            // If serialization fails, just return the type name
            return $"[{typeof(TRequest).Name} - Serialization failed]";
        }
    }
}
