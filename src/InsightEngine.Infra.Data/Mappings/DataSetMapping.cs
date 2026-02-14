using InsightEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsightEngine.Infra.Data.Mappings;

public class DataSetMapping : IEntityTypeConfiguration<DataSet>
{
    public void Configure(EntityTypeBuilder<DataSet> builder)
    {
        builder.ToTable("DataSets");

        builder.HasKey(ds => ds.Id);

        builder.Property(ds => ds.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(ds => ds.StoredFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(ds => ds.StoredFileName)
            .IsUnique();

        builder.Property(ds => ds.StoredPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(ds => ds.FileSizeInBytes)
            .IsRequired();

        builder.Property(ds => ds.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ds => ds.CreatedAt)
            .IsRequired();

        builder.Property(ds => ds.UpdatedAt);
    }
}
