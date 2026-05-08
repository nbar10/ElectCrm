namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(200).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        // Unique email within a brand.
        builder.HasIndex(e => new { e.AgencyBrandId, e.Email }).IsUnique();
        builder.HasIndex(e => e.AgencyBrandId);

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
