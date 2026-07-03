using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Vouchers.Configurations;

internal sealed class QrParametersConfiguration : IEntityTypeConfiguration<QrParameters>
{
    public void Configure(EntityTypeBuilder<QrParameters> builder)
    {
        builder.ToTable("qr_parameters");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Version)
            .HasColumnName("version");

        builder.Property(e => e.EccLevel)
            .HasColumnName("ecc_level")
            .HasMaxLength(1)
            .IsRequired();

        builder.Property(e => e.MaskPattern)
            .HasColumnName("mask_pattern");

        builder.Property(e => e.EncodingMode)
            .HasColumnName("encoding_mode")
            .HasMaxLength(16);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(e => new { e.EccLevel, e.Version, e.MaskPattern, e.EncodingMode })
            .IsUnique();
    }
}
