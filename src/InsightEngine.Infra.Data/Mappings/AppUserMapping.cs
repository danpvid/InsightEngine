using InsightEngine.Infra.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InsightEngine.Infra.Data.Mappings;

public class AppUserMapping : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.NormalizedUserName).HasMaxLength(256);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256);
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.SecurityStamp).HasMaxLength(256);
        builder.Property(x => x.ConcurrencyStamp).HasMaxLength(256);

        builder.HasIndex(x => x.NormalizedEmail)
            .HasDatabaseName("IX_Users_NormalizedEmail");

        builder.HasIndex(x => x.NormalizedUserName)
            .HasDatabaseName("IX_Users_NormalizedUserName")
            .IsUnique();

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.AvatarUrl)
            .HasMaxLength(1024);

        builder.Property(x => x.Plan)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Free");

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt);
    }
}
