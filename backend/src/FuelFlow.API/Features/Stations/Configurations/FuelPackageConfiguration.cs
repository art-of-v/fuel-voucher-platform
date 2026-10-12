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
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Liters)
            .HasColumnName("liters")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        builder.Property(e => e.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.OriginalPrice)
            .HasColumnName("original_price")
            .HasColumnType("numeric(12,2)")
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

        builder.Property(e => e.PumpPricePerLiter)
            .HasColumnName("pump_price_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.MinDiscountPerLiter)
            .HasColumnName("min_discount_per_liter")
            .HasColumnType("numeric(10,4)");

        builder.Property(e => e.PriceUpdatedAt)
            .HasColumnName("price_updated_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.PriceUpdatedByUserId)
            .HasColumnName("price_updated_by_user_id");

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // NOT folded into the query filter below on purpose - see FuelPackage.IsTestData. A global
        // filter would hide rehearsal rows from the admin screens that have to manage them.
        builder.Property(e => e.IsTestData).HasColumnName("is_test_data").IsRequired();

        // The storefront query filters on it, so it carries the filter columns in front.
        builder.HasIndex(e => new { e.IsDeleted, e.IsTestData });

        // A package is the thing an order line points at; archiving keeps that line readable.
        builder.HasQueryFilter(e => !e.IsDeleted);

        builder.HasIndex(e => e.StationId);
        builder.HasIndex(e => e.FuelTypeId);
        builder.HasIndex(e => new { e.StationId, e.FuelTypeId });

        // Natural key ("okko-95-10"), bounded by CHECK rather than a type change - see
        // StationConfiguration for why that trade is worth taking.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fuel_packages_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_fuel_packages_station_id_length", "length(station_id) <= 100");
            t.HasCheckConstraint("ck_fuel_packages_fuel_type_id_length", "length(fuel_type_id) <= 100");
        });
    }
}

