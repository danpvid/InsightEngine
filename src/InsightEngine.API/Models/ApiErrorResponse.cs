namespace InsightEngine.API.Models;

/// <summary>
/// Standard error response envelope for all API errors
/// </summary>
public class ApiErrorResponse
{
    /// <summary>
    /// Always false for error responses
    /// </summary>
    public bool Success { get; set; } = false;

    /// <summary>
    /// Always null for error responses
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Machine-readable error code
    /// </summary>
    public string Code { get; set; } = "internal_error";

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Field-level error details
    /// </summary>
    public Dictionary<string, List<string>>? Details { get; set; }

    /// <summary>
    /// Request trace ID
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    public ApiErrorResponse()
    {
    }

    public ApiErrorResponse(string code, string message, string traceId)
    {
        Code = code;
        Message = message;
        TraceId = traceId;
    }

    public static ApiErrorResponse FromMessage(string message, string traceId, string code = "internal_error")
    {
        return new ApiErrorResponse(code, message, traceId);
    }

    public static ApiErrorResponse FromValidationErrors(Dictionary<string, List<string>> errors, string traceId)
    {
        var firstError = errors.Values.FirstOrDefault()?.FirstOrDefault() ?? "Validation failed";
        return new ApiErrorResponse
        {
            Code = "validation_error",
            Message = firstError,
            Details = errors,
            TraceId = traceId
        };
    }

    public static ApiErrorResponse FromList(List<string> errors, string traceId)
    {
        var firstError = errors.FirstOrDefault() ?? "Operation failed";
        return new ApiErrorResponse
        {
            Code = "operation_error",
            Message = firstError,
            Details = new Dictionary<string, List<string>> { ["Errors"] = errors },
            TraceId = traceId
        };
    }

    public static ApiErrorResponse NotFound(string message, string traceId)
    {
        return new ApiErrorResponse("not_found", message, traceId);
    }
}
