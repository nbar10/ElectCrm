namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Placements;
using ElectCrm.Domain.Users;
using ElectCrm.Domain.Vacancies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class PlacementConfiguration : IEntityTypeConfiguration<Placement>
{
    public void Configure(EntityTypeBuilder<Placement> builder)
    {
        builder.ToTable("Placements");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Vacancy)
            .WithMany()
            .HasForeignKey(e => e.VacancyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Candidate)
            .WithMany()
            .HasForeignKey(e => e.CandidateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ConsultantOwner)
            .WithMany()
            .HasForeignKey(e => e.ConsultantOwnerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.Property(e => e.ReferenceNumber)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.SnapshotTakenAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.BillRate)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.SnapshotBillRate)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.ProposedStartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.ActualStartDate)
            .HasColumnType("date");

        builder.Property(e => e.ExpectedEndDate)
            .HasColumnType("date");

        builder.Property(e => e.ActualEndDate)
            .HasColumnType("date");

        builder.Property(e => e.HoursPerWeek)
            .HasColumnType("decimal(5,2)")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.StatusReason)
            .HasMaxLength(500);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("datetimeoffset")
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

        builder.OwnsOne(e => e.SnapshotSourceVacancyPayRate, sr =>
        {
            sr.Property(p => p.Amount)
                .HasColumnName("Snapshot_PayRate_Amount")
                .HasColumnType("decimal(18,4)")
                .IsRequired();

            sr.Property(p => p.Currency)
                .HasColumnName("Snapshot_PayRate_Currency")
                .HasMaxLength(3)
                .IsRequired();

            sr.Property(p => p.EngagementType)
                .HasColumnName("Snapshot_PayRate_EngagementType")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            sr.Property(p => p.HolidayPayInclusive)
                .HasColumnName("Snapshot_PayRate_HolidayPayInclusive")
                .IsRequired();

            sr.Property(p => p.HolidayPayRate)
                .HasColumnName("Snapshot_PayRate_HolidayPayRate")
                .HasColumnType("decimal(18,4)");
        });

        builder.HasIndex(e => e.AgencyBrandId)
            .HasDatabaseName("IX_Placements_AgencyBrandId");

        builder.HasIndex(e => new { e.AgencyBrandId, e.ReferenceNumber })
            .IsUnique()
            .HasDatabaseName("IX_Placements_AgencyBrandId_ReferenceNumber");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status })
            .HasDatabaseName("IX_Placements_AgencyBrandId_Status");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status, e.ProposedStartDate })
            .HasDatabaseName("IX_Placements_AgencyBrandId_Status_ProposedStartDate");

        builder.HasIndex(e => e.CandidateId)
            .HasDatabaseName("IX_Placements_CandidateId");

        builder.HasIndex(e => new { e.CandidateId, e.Status })
            .HasDatabaseName("IX_Placements_CandidateId_Status");

        builder.HasIndex(e => e.VacancyId)
            .HasDatabaseName("IX_Placements_VacancyId");

        builder.HasIndex(e => new { e.VacancyId, e.Status })
            .HasDatabaseName("IX_Placements_VacancyId_Status");

        builder.HasIndex(e => e.ConsultantOwnerId)
            .HasDatabaseName("IX_Placements_ConsultantOwnerId");
    }
}
