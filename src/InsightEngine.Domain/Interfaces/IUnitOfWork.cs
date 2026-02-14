namespace InsightEngine.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<bool> CommitAsync();
    Task RollbackAsync();
}
