namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Contacts;
using ElectCrm.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("Contacts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();
        builder.Property(e => e.ClientId).IsRequired();

        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.RoleTitle).HasMaxLength(200);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(30);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        builder.Property(e => e.PrimaryForCategories)
            .HasField("_categories")
            .HasConversion(new ContactCategoryListConverter())
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.OwnsOne(e => e.CommunicationPreferences, owned =>
        {
            owned.Property(p => p.Channel)
                .HasColumnName("ChannelPrefs_Channel")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            owned.Property(p => p.PreferredDays)
                .HasColumnName("ChannelPrefs_PreferredDays")
                .HasMaxLength(50);

            owned.Property(p => p.QuietHoursStart)
                .HasColumnName("ChannelPrefs_QuietHoursStart");

            owned.Property(p => p.QuietHoursEnd)
                .HasColumnName("ChannelPrefs_QuietHoursEnd");
        });

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => e.AgencyBrandId);
        builder.HasIndex(e => e.ClientId);
        builder.HasIndex(e => new { e.AgencyBrandId, e.Status });
        builder.HasIndex(e => new { e.AgencyBrandId, e.ClientId });

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
