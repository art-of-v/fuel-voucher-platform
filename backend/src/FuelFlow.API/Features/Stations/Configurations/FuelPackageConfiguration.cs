using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class FuelPackageConfiguration : IEntityTypeConfiguration<FuelPackage>
{
    public void Configure(EntityTypeBuilder<FuelPackage> builder)
    {
        builder.ToTable("fuel_packages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnType("text");

        builder.Property(e => e.StationId)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.FuelName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.Price)
            .IsRequired();

        builder.Property(e => e.OriginalPrice)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Per-liter pricing columns
        builder.Property(e => e.SupplierPricePerLiter)
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.MarginUahPerLiter)
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.MarginPercent)
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.FinalPricePerLiter)
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.PriceUpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.PriceUpdatedByUserId);

        builder.HasIndex(e => e.StationId);
        builder.HasIndex(e => e.FuelTypeId);
    }
}

