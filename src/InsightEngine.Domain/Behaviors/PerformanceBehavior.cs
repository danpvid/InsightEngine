using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace InsightEngine.Domain.Behaviors;

/// <summary>
/// Pipeline behavior that tracks performance metrics for all commands/queries
/// and logs slow operations
/// </summary>
public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private const int SlowRequestThresholdMs = 1000; // 1 second

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Started executing {RequestName}",
            requestName);

        try
        {
            var response = await next();
            
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                _logger.LogWarning(
                    "Slow request detected: {RequestName} took {ElapsedMs}ms (threshold: {ThresholdMs}ms)",
                    requestName,
                    stopwatch.ElapsedMilliseconds,
                    SlowRequestThresholdMs);
            }
            else
            {
                _logger.LogInformation(
                    "Request {RequestName} completed successfully in {ElapsedMs}ms",
                    requestName,
                    stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(
                ex,
                "Request {RequestName} failed after {ElapsedMs}ms with exception: {ExceptionMessage}",
                requestName,
                stopwatch.ElapsedMilliseconds,
                ex.Message);
            
            throw;
        }
    }
}
