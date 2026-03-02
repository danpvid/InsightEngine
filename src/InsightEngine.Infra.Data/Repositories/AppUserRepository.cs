using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace InsightEngine.Infra.Data.Repositories;

public class AppUserRepository : Repository<AppUser>, IAppUserRepository
{
    public AppUserRepository(InsightEngineContext context) : base(context)
    {
    }

    public async Task<AppUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await _dbSet.FirstOrDefaultAsync(x => x.Email == normalized, cancellationToken);
    }

    public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}
