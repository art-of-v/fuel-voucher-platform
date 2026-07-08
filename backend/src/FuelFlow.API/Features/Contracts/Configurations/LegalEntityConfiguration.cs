using FuelFlow.Features.Contracts.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Contracts.Configurations;

internal sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        builder.ToTable("legal_entities");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Edrpou)
            .HasColumnName("edrpou")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.VatNumber)
            .HasColumnName("vat_number")
            .HasMaxLength(20);

        builder.Property(e => e.Address)
            .HasColumnName("address")
            .HasMaxLength(500);

        builder.Property(e => e.DirectorName)
            .HasColumnName("director_name")
            .HasMaxLength(200);

        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20);

        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId)
            .IsUnique();

        builder.HasIndex(e => e.Edrpou)
            .IsUnique();
    }
}

