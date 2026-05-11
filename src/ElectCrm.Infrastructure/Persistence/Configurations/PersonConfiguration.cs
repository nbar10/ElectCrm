namespace ElectCrm.Infrastructure.Persistence.Configurations;

using ElectCrm.Domain.Persons;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// PersonIdentity is intentionally cross-tenant — no AgencyBrandId filter is applied here.
// See Plan 03 and §3.5 of the canonical data model.
public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("Persons");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.FullNameNormalised).HasMaxLength(500).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.PrimaryPhoneHash).HasColumnType("nchar(64)");
        builder.Property(e => e.NationalInsuranceNumberHash).HasColumnType("nchar(64)");
        builder.Property(e => e.PassportNumberHash).HasColumnType("nchar(64)");

        builder.Property(e => e.PrimaryPhoneEncrypted).HasColumnType("nvarchar(max)");
        builder.Property(e => e.NationalInsuranceNumberEncrypted).HasColumnType("nvarchar(max)");
        builder.Property(e => e.PassportNumberEncrypted).HasColumnType("nvarchar(max)");

        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        builder.Ignore(e => e.DomainEvents);

        builder.HasIndex(e => e.PrimaryPhoneHash);
        builder.HasIndex(e => e.NationalInsuranceNumberHash);
        builder.HasIndex(e => e.PassportNumberHash);
        builder.HasIndex(e => new { e.FullNameNormalised, e.DateOfBirth });
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.IsDeleted);
        builder.HasIndex(e => e.CreatedAt);
    }
}
