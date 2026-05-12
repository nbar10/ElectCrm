namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PrimaryBranch)
            .WithMany()
            .HasForeignKey(e => e.PrimaryBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.LegalName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.TradingName)
            .HasMaxLength(200);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.IsDeleted).IsRequired();

        builder.Property(e => e.DeletedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => e.AgencyBrandId)
            .HasDatabaseName("IX_Clients_AgencyBrandId");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status })
            .HasDatabaseName("IX_Clients_AgencyBrandId_Status");

        builder.HasIndex(e => e.PrimaryBranchId)
            .HasDatabaseName("IX_Clients_PrimaryBranchId");

        builder.HasIndex(e => e.IsDeleted)
            .HasDatabaseName("IX_Clients_IsDeleted");
    }
}
