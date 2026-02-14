using InsightEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InsightEngine.Infra.Data.Context;

public class InsightEngineContext : DbContext
{
    public InsightEngineContext(DbContextOptions<InsightEngineContext> options) : base(options)
    {
    }

    // DbSets
    public DbSet<DataSet> DataSets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Aplicar configurações
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InsightEngineContext).Assembly);

        // Convenções
        foreach (var relationship in modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys()))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries()
            .Where(entry => entry.Entity.GetType().GetProperty("CreatedAt") != null))
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property("CreatedAt").IsModified = false;
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
