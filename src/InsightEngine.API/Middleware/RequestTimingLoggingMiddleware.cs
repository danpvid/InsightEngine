using System.Diagnostics;

namespace InsightEngine.API.Middleware;

public class RequestTimingLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingLoggingMiddleware> _logger;

    public RequestTimingLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestTimingLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "HTTP request started TraceId={TraceId} Method={Method} Path={Path}",
            traceId,
            context.Request.Method,
            context.Request.Path);

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            _logger.LogInformation(
                "HTTP request completed TraceId={TraceId} Method={Method} Path={Path} StatusCode={StatusCode} DurationMs={DurationMs}",
                traceId,
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
