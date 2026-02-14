using InsightEngine.Domain.Core;
using MediatR;

namespace InsightEngine.Domain.Commands;

/// <summary>
/// Base class for all commands that return a result
/// </summary>
public abstract class Command : IRequest<Result>
{
    public DateTime Timestamp { get; private set; }

    protected Command()
    {
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Base class for all commands that return a result with data
/// </summary>
public abstract class Command<TResponse> : IRequest<Result<TResponse>>
{
    public DateTime Timestamp { get; private set; }

    protected Command()
    {
        Timestamp = DateTime.UtcNow;
    }
}
