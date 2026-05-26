namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.Workers;
using ElectCrm.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class WorkerProfileConfiguration : IEntityTypeConfiguration<WorkerProfile>
{
    public void Configure(EntityTypeBuilder<WorkerProfile> builder)
    {
        builder.ToTable("WorkerProfiles");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ApplicationUserId).IsRequired();

        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.MiddleName).HasMaxLength(100).IsRequired(false);
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();

        // NOTE: Do not add Any Encryption configuration here — the Always Encrypted ENCRYPTED WITH
        // clause is applied via a raw migration SQL amendment only. EF treats this as a plain nvarchar(10).
        builder.Property(e => e.NationalInsuranceNumber).HasMaxLength(10).IsRequired();

        builder.Property(e => e.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(e => e.AddressLine2).HasMaxLength(200).IsRequired(false);
        builder.Property(e => e.City).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Postcode).HasMaxLength(10).IsRequired();

        builder.Property(e => e.DateOfBirth).HasColumnType("date").IsRequired();

        builder.Property(e => e.RightToWorkDeclaredAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnType("datetimeoffset").IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.ApplicationUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ApplicationUserId)
            .IsUnique()
            .HasDatabaseName("IX_WorkerProfiles_ApplicationUserId");
    }
}
