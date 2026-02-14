using MediatR;

namespace InsightEngine.Application.Commands;

public abstract class Command : IRequest<bool>
{
    public DateTime Timestamp { get; private set; }

    protected Command()
    {
        Timestamp = DateTime.UtcNow;
    }

    public abstract bool IsValid();
}
