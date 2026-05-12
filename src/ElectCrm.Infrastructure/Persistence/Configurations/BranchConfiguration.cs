namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();

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

        builder.Property(e => e.Geography)
            .HasConversion<GeoAreaConverter>()
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.OwnsOne(e => e.Address, addr =>
        {
            addr.Property(a => a.Line1).HasColumnName("AddressLine1").HasMaxLength(200).IsRequired();
            addr.Property(a => a.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
            addr.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(100).IsRequired();
            addr.Property(a => a.County).HasColumnName("AddressCounty").HasMaxLength(100);
            addr.Property(a => a.Postcode).HasColumnName("AddressPostcode").HasMaxLength(10).IsRequired();
            addr.Property(a => a.Country).HasColumnName("AddressCountry").HasMaxLength(2).IsRequired();
        });

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        // Unique composite index enforces branch name uniqueness within a brand.
        // Replaces the prior non-unique IX_Branches_AgencyBrandId.
        builder.HasIndex(e => new { e.AgencyBrandId, e.Name }).IsUnique()
            .HasDatabaseName("IX_Branches_AgencyBrandId_Name");
        builder.HasIndex(e => new { e.AgencyBrandId, e.Status });

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
