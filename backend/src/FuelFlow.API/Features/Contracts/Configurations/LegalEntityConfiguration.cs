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

        builder.Property(e => e.Id);

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Edrpou)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.VatNumber)
            .HasMaxLength(20);

        builder.Property(e => e.Address)
            .HasMaxLength(500);

        builder.Property(e => e.DirectorName)
            .HasMaxLength(200);

        builder.Property(e => e.Phone)
            .HasMaxLength(20);

        builder.Property(e => e.Email)
            .HasMaxLength(200);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
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

