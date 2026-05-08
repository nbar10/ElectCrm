namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class UserInviteConfiguration : IEntityTypeConfiguration<UserInvite>
{
    public void Configure(EntityTypeBuilder<UserInvite> builder)
    {
        builder.ToTable("UserInvites");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();
        builder.Property(e => e.InvitedEmail).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Token).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        // Token is used as the invite URL parameter — must be globally unique.
        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasIndex(e => new { e.AgencyBrandId, e.InvitedEmail });
        builder.HasIndex(e => new { e.AgencyBrandId, e.Status });

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
