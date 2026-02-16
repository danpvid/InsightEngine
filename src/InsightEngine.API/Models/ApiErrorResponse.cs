using Microsoft.AspNetCore.Http;

namespace InsightEngine.API.Models;

/// <summary>
/// Standard error response envelope for all API errors.
/// </summary>
public class ApiErrorResponse
{
    public bool Success { get; set; } = false;
    public List<ApiErrorItem> Errors { get; set; } = new();
    public string TraceId { get; set; } = string.Empty;
    public int Status { get; set; }

    public static ApiErrorResponse FromMessage(
        string message,
        string traceId,
        string code = "internal_error",
        int status = StatusCodes.Status500InternalServerError,
        string? target = null)
    {
        return new ApiErrorResponse
        {
            TraceId = traceId,
            Status = status,
            Errors =
            [
                new ApiErrorItem
                {
                    Code = code,
                    Message = message,
                    Target = target
                }
            ]
        };
    }

    public static ApiErrorResponse FromList(
        IReadOnlyCollection<string> errors,
        string traceId,
        int status = StatusCodes.Status400BadRequest,
        string code = "operation_error")
    {
        var mapped = errors.Count == 0
            ? [new ApiErrorItem { Code = code, Message = "Operation failed." }]
            : errors.Select(message => new ApiErrorItem { Code = code, Message = message }).ToList();

        return new ApiErrorResponse
        {
            TraceId = traceId,
            Status = status,
            Errors = mapped
        };
    }

    public static ApiErrorResponse FromValidationErrors(
        Dictionary<string, List<string>> errors,
        string traceId,
        int status = StatusCodes.Status400BadRequest)
    {
        var mapped = new List<ApiErrorItem>();
        foreach (var (target, messages) in errors)
        {
            foreach (var message in messages)
            {
                mapped.Add(new ApiErrorItem
                {
                    Code = "validation_error",
                    Message = message,
                    Target = target
                });
            }
        }

        if (mapped.Count == 0)
        {
            mapped.Add(new ApiErrorItem
            {
                Code = "validation_error",
                Message = "Validation failed."
            });
        }

        return new ApiErrorResponse
        {
            TraceId = traceId,
            Status = status,
            Errors = mapped
        };
    }

    public static ApiErrorResponse NotFound(string message, string traceId)
    {
        return FromMessage(message, traceId, "not_found", StatusCodes.Status404NotFound);
    }
}

public class ApiErrorItem
{
    public string Code { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public string? Target { get; set; }
}
