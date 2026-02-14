using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Context;

namespace InsightEngine.Infra.Data.UoW;

public class UnitOfWork : IUnitOfWork
{
    private readonly InsightEngineContext _context;

    public UnitOfWork(InsightEngineContext context)
    {
        _context = context;
    }

    public async Task<bool> CommitAsync()
    {
        try
        {
            var success = await _context.SaveChangesAsync() > 0;
            return success;
        }
        catch (Exception)
        {
            await RollbackAsync();
            throw;
        }
    }

    public async Task RollbackAsync()
    {
        await Task.Run(() =>
        {
            foreach (var entry in _context.ChangeTracker.Entries())
            {
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
            }
        });
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
