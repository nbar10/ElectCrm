namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class AgencyBrandConfiguration : IEntityTypeConfiguration<AgencyBrand>
{
    public void Configure(EntityTypeBuilder<AgencyBrand> builder)
    {
        builder.ToTable("AgencyBrands");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TradingName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CompaniesHouseNumber).HasMaxLength(10).IsRequired();
        builder.Property(e => e.VatNumber).HasMaxLength(20);
        builder.Property(e => e.GlaaLicenceNumber).HasMaxLength(50);
        builder.Property(e => e.PrimaryContactEmail).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DwpAccountId).HasMaxLength(100);
        builder.Property(e => e.AgentPersonaName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.OnCallContactPhone).HasMaxLength(20);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.OwnsOne(e => e.RegisteredAddress, addr =>
        {
            addr.Property(a => a.Line1).HasColumnName("RegisteredAddressLine1").HasMaxLength(200).IsRequired();
            addr.Property(a => a.Line2).HasColumnName("RegisteredAddressLine2").HasMaxLength(200);
            addr.Property(a => a.City).HasColumnName("RegisteredAddressCity").HasMaxLength(100).IsRequired();
            addr.Property(a => a.County).HasColumnName("RegisteredAddressCounty").HasMaxLength(100);
            addr.Property(a => a.Postcode).HasColumnName("RegisteredAddressPostcode").HasMaxLength(10).IsRequired();
            addr.Property(a => a.Country).HasColumnName("RegisteredAddressCountry").HasMaxLength(2).IsRequired();
        });

        // TenantId is computed from Id — not a stored column.
        builder.Ignore(e => e.TenantId);
        // Domain events are in-memory only.
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => e.CompaniesHouseNumber).IsUnique();
        builder.HasIndex(e => e.Status);
    }
}
