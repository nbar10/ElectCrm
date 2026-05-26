namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Users;
using ElectCrm.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // AspNetUsers table name is set by IdentityDbContext — we only add our extra column here.
        builder.Property(e => e.DomainUserId).IsRequired();

        builder.HasIndex(e => e.DomainUserId).IsUnique();

        // FK to the Domain User entity.
        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<ApplicationUser>(e => e.DomainUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(100)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(e => e.JobTitle)
            .HasMaxLength(100);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        builder.Property(e => e.RequirePasswordChange)
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(e => e.UpdatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // FK: PrimaryBranchId → Branches.Id
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(u => u.PrimaryBranchId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Self-referential FK: LastModifiedById → AspNetUsers.Id
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(u => u.LastModifiedById)
            // SQL Server rejects ON DELETE SET NULL on self-referential FKs (cascade cycle check).
            // ClientSetNull: EF nulls the FK in tracked entities before SaveChangesAsync.
            // Hard-deletes via UserManager.DeleteAsync would FK-violate — users are soft-deactivated
            // only, so this is acceptable. Revisit if hard-delete is ever introduced.
            .OnDelete(DeleteBehavior.ClientSetNull)
            .IsRequired(false);

        builder.HasIndex(u => u.PrimaryBranchId)
            .HasDatabaseName("IX_AspNetUsers_PrimaryBranchId");

        builder.HasIndex(u => u.IsActive)
            .HasDatabaseName("IX_AspNetUsers_IsActive");

        builder.HasIndex(u => new { u.PrimaryBranchId, u.IsActive })
            .HasDatabaseName("IX_AspNetUsers_PrimaryBranchId_IsActive");
    }
}
