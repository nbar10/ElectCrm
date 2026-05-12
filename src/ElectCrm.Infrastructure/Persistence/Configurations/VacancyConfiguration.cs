namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Branches;
using ElectCrm.Domain.Clients;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.ToTable("Vacancies");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Branch)
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ConsultantOwner)
            .WithMany()
            .HasForeignKey(e => e.ConsultantOwnerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.Property(e => e.ReferenceNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RoleTitle)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(5000);

        builder.Property(e => e.ShiftPattern)
            .HasMaxLength(200);

        builder.Property(e => e.RequiredCards)
            .HasMaxLength(500);

        builder.Property(e => e.StatusReason)
            .HasMaxLength(500);

        builder.Property(e => e.HeadcountRequired).IsRequired();

        builder.Property(e => e.BillRate)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.CreatedFrom)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

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

        builder.OwnsOne(e => e.PayRate, pr =>
        {
            pr.Property(p => p.Amount)
                .HasColumnName("PayRate_Amount")
                .HasColumnType("decimal(18,4)")
                .IsRequired();

            pr.Property(p => p.Currency)
                .HasColumnName("PayRate_Currency")
                .HasMaxLength(3)
                .IsRequired();

            pr.Property(p => p.EngagementType)
                .HasColumnName("PayRate_EngagementType")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            pr.Property(p => p.HolidayPayInclusive)
                .HasColumnName("PayRate_HolidayPayInclusive")
                .IsRequired();

            pr.Property(p => p.HolidayPayRate)
                .HasColumnName("PayRate_HolidayPayRate")
                .HasColumnType("decimal(18,4)");
        });

        builder.OwnsOne(e => e.Location, loc =>
        {
            loc.Property(l => l.Postcode)
                .HasColumnName("LocationPostcode")
                .HasMaxLength(10)
                .IsRequired();

            loc.Property(l => l.Description)
                .HasColumnName("LocationDescription")
                .HasMaxLength(200);
        });

        builder.HasIndex(e => e.AgencyBrandId)
            .HasDatabaseName("IX_Vacancies_AgencyBrandId");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status })
            .HasDatabaseName("IX_Vacancies_AgencyBrandId_Status");

        builder.HasIndex(e => e.BranchId)
            .HasDatabaseName("IX_Vacancies_BranchId");

        builder.HasIndex(e => e.ClientId)
            .HasDatabaseName("IX_Vacancies_ClientId");

        builder.HasIndex(e => e.ConsultantOwnerId)
            .HasDatabaseName("IX_Vacancies_ConsultantOwnerId");

        builder.HasIndex(e => new { e.AgencyBrandId, e.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("IX_Vacancies_AgencyBrandId_ReferenceNumber");

        builder.HasIndex(e => e.StartDate)
            .HasDatabaseName("IX_Vacancies_StartDate");
    }
}
