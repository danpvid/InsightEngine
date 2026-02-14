using MediatR;

namespace InsightEngine.Application.Queries;

public abstract class Query<TResponse> : IRequest<TResponse>
{
}
