using InsightEngine.Domain.Core.Models;

namespace InsightEngine.Domain.Interfaces;

public interface IRepository<T> where T : Entity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);
}
