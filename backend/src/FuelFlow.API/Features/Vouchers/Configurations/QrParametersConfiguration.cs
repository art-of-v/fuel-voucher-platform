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

        builder.Property(e => e.Id);

        builder.Property(e => e.Version);

        builder.Property(e => e.EccLevel)
            .HasMaxLength(1)
            .IsRequired();

        builder.Property(e => e.MaskPattern);

        builder.Property(e => e.EncodingMode)
            .HasMaxLength(16);

        builder.Property(e => e.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(e => new { e.EccLevel, e.Version, e.MaskPattern, e.EncodingMode })
            .IsUnique();
    }
}
