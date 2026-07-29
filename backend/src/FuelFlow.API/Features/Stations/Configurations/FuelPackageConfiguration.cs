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
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.FuelTypeId)
            .HasColumnName("fuel_type_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.FuelName)
            .HasColumnName("fuel_name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.Price)
            .HasColumnName("price")
            .IsRequired();

        builder.Property(e => e.OriginalPrice)
            .HasColumnName("original_price")
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Per-liter pricing columns
        builder.Property(e => e.SupplierPricePerLiter)
            .HasColumnName("supplier_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.MarginUahPerLiter)
            .HasColumnName("margin_uah_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.MarginPercent)
            .HasColumnName("margin_percent")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.FinalPricePerLiter)
            .HasColumnName("final_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.PriceUpdatedAt)
            .HasColumnName("price_updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.PriceUpdatedByUserId)
            .HasColumnName("price_updated_by_user_id");

        builder.HasIndex(e => e.StationId);
        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => new { e.StationId, e.FuelTypeId });
    }
}

