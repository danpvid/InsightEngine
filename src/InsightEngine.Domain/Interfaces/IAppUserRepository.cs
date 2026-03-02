using InsightEngine.Domain.Entities;

namespace InsightEngine.Domain.Interfaces;

public interface IAppUserRepository : IRepository<AppUser>
{
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
