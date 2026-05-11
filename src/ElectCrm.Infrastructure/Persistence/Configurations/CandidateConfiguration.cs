namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.AgencyBrands;
using ElectCrm.Domain.Candidates;
using ElectCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("Candidates");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AgencyBrandId).IsRequired();

        builder.HasOne<AgencyBrand>()
            .WithMany()
            .HasForeignKey(e => e.AgencyBrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OwnerConsultant)
            .WithMany()
            .HasForeignKey(e => e.OwnerConsultantId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RegistrationDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(e => e.PrimaryTrade).HasMaxLength(100);
        builder.Property(e => e.Source).HasMaxLength(100);
        builder.Property(e => e.SourceLegacyId).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        builder.Ignore(e => e.TenantId);
        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => new { e.PersonId, e.AgencyBrandId })
            .IsUnique()
            .HasDatabaseName("IX_Candidates_PersonId_AgencyBrandId")
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(e => e.AgencyBrandId)
            .HasDatabaseName("IX_Candidates_AgencyBrandId");

        builder.HasIndex(e => new { e.AgencyBrandId, e.Status })
            .HasDatabaseName("IX_Candidates_AgencyBrandId_Status");

        builder.HasIndex(e => e.PersonId)
            .HasDatabaseName("IX_Candidates_PersonId");

        builder.HasIndex(e => e.OwnerConsultantId)
            .HasDatabaseName("IX_Candidates_OwnerConsultantId");

        builder.HasIndex(e => e.RegistrationDate)
            .HasDatabaseName("IX_Candidates_RegistrationDate");

        builder.HasIndex(e => e.IsDeleted)
            .HasDatabaseName("IX_Candidates_IsDeleted");
    }
}
