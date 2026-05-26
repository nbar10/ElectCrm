namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.Compliance;
using ElectCrm.Domain.Persons;
using ElectCrm.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> builder)
    {
        builder.ToTable("ComplianceDocuments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.OtherDescription).HasMaxLength(200);
        builder.Property(e => e.DocumentReference).HasMaxLength(100);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RejectionReason).HasMaxLength(1000);
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.Property(e => e.IssueDate).HasColumnType("date").IsRequired(false);
        builder.Property(e => e.ExpiryDate).HasColumnType("date").IsRequired(false);

        builder.Property(e => e.VerifiedAt).HasColumnType("datetimeoffset").IsRequired(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnType("datetimeoffset").IsRequired();

        builder.Property(e => e.LastModifiedById).IsRequired();

        builder.Ignore(e => e.DomainEvents);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.VerifiedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.LastModifiedById)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(e => e.PersonId)
            .HasDatabaseName("IX_ComplianceDocuments_PersonId");

        builder.HasIndex(e => new { e.PersonId, e.DocumentType })
            .HasDatabaseName("IX_ComplianceDocuments_PersonId_DocumentType");

        builder.HasIndex(e => e.ExpiryDate)
            .HasDatabaseName("IX_ComplianceDocuments_ExpiryDate");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_ComplianceDocuments_Status");
    }
}
