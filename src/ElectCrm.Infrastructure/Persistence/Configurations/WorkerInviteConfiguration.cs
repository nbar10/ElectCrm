namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Persons;
using ElectCrm.Domain.Workers;
using ElectCrm.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class WorkerInviteConfiguration : IEntityTypeConfiguration<WorkerInvite>
{
    public void Configure(EntityTypeBuilder<WorkerInvite> builder)
    {
        builder.ToTable("WorkerInvites");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();

        builder.Property(e => e.Token)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.PrefillFirstName).HasMaxLength(100);
        builder.Property(e => e.PrefillLastName).HasMaxLength(100);
        builder.Property(e => e.PrefillEmail).HasMaxLength(200);
        builder.Property(e => e.PrefillPhone).HasMaxLength(20);

        builder.Property(e => e.CreatedAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(e => e.ConsumedAt).HasColumnType("datetimeoffset").IsRequired(false);

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.CreatedByConsultantId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(e => e.ExistingPersonId)
            // ClientSetNull: SQL Server rejects multiple SET NULL paths from Persons to WorkerInvites.
            // EF nulls this in tracked entities; NO ACTION at DB level is safe (Persons are soft-deleted only).
            .OnDelete(DeleteBehavior.ClientSetNull)
            .IsRequired(false);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(e => e.ConsumedByPersonId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasIndex(e => e.Token)
            .IsUnique()
            .HasDatabaseName("IX_WorkerInvites_Token");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status })
            .HasDatabaseName("IX_WorkerInvites_AgencyBrandId_Status");

        builder.HasIndex(e => e.CreatedByConsultantId)
            .HasDatabaseName("IX_WorkerInvites_CreatedByConsultantId");

        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_WorkerInvites_ExpiresAt");
    }
}
