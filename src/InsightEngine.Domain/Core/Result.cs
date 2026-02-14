namespace InsightEngine.Domain.Core;

/// <summary>
/// Represents the result of an operation with success/failure status
/// </summary>
public class Result
{
    public bool IsSuccess { get; protected set; }
    public bool IsFailure => !IsSuccess;
    public string Message { get; protected set; } = string.Empty;
    public List<string> Errors { get; protected set; } = new();

    protected Result() { }

    protected Result(bool isSuccess, string message, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Message = message;
        Errors = errors ?? new List<string>();
    }

    public static Result Success(string message = "Operation completed successfully")
        => new(true, message);

    public static Result Failure(string error)
        => new(false, error, new List<string> { error });

    public static Result Failure(List<string> errors)
        => new(false, "Operation failed with errors", errors);

    public static Result<T> Success<T>(T data, string message = "Operation completed successfully")
        => new(true, message, data);

    public static Result<T> Failure<T>(string error)
        => new(false, error, default, new List<string> { error });

    public static Result<T> Failure<T>(List<string> errors)
        => new(false, "Operation failed with errors", default, errors);
}

/// <summary>
/// Represents the result of an operation with data payload
/// </summary>
public class Result<T> : Result
{
    public T? Data { get; private set; }

    internal Result(bool isSuccess, string message, T? data, List<string>? errors = null)
        : base(isSuccess, message, errors)
    {
        Data = data;
    }
}
