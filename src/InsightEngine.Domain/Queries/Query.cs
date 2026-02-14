using InsightEngine.Domain.Core;
using MediatR;

namespace InsightEngine.Domain.Queries;

/// <summary>
/// Base class for all queries
/// </summary>
public abstract class Query<TResponse> : IRequest<Result<TResponse>>
{
    public DateTime Timestamp { get; private set; }

    protected Query()
    {
        Timestamp = DateTime.UtcNow;
    }
}
