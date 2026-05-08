namespace ElectCrm.Infrastructure.Persistence.Configurations;

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
    }
}
