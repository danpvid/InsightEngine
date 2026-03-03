using InsightEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsightEngine.Infra.Data.Mappings;

public class DashboardCacheEntryMapping : IEntityTypeConfiguration<DashboardCacheEntry>
{
    public void Configure(EntityTypeBuilder<DashboardCacheEntry> builder)
    {
        builder.ToTable("DashboardCache");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.OwnerUserId)
            .IsRequired();

        builder.Property(entry => entry.DatasetId)
            .IsRequired();

        builder.Property(entry => entry.Version)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(entry => entry.PayloadJson)
            .IsRequired();

        builder.Property(entry => entry.SourceDatasetUpdatedAt)
            .IsRequired();

        builder.Property(entry => entry.SourceFingerprint)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(entry => entry.CreatedAt)
            .IsRequired();

        builder.Property(entry => entry.UpdatedAt);

        builder.HasIndex(entry => new { entry.OwnerUserId, entry.DatasetId, entry.Version })
            .IsUnique();
    }
}
