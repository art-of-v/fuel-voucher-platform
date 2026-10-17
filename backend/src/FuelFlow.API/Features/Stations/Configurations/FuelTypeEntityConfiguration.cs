using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Stations.Configurations;

internal sealed class FuelTypeEntityConfiguration : IEntityTypeConfiguration<FuelTypeEntity>
{
    public void Configure(EntityTypeBuilder<FuelTypeEntity> builder)
    {
        builder.ToTable("fuel_types");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // fuel_vouchers.fuel_type_id cascades from here, so archiving rather than removing is what
        // keeps paid-for vouchers in existence (#217).
        builder.HasQueryFilter(e => !e.IsDeleted);


        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("text");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.StationId)
            .HasColumnName("station_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.BrandFuelCode)
            .HasColumnName("brand_fuel_code")
            .HasColumnType("text");

        builder.Property(e => e.BasePrice)
            .HasColumnName("base_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.DiscountPrice)
            .HasColumnName("discount_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(e => e.AllowBelowCost)
            .HasColumnName("allow_below_cost")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.StationId);

        // (station_id, name), unique and partial on NOT is_deleted. A natural key used to give "one
        // fuel per name per station" for free — the id was 'okko-95', so a second A-95 at OKKO collided
        // on the primary key — and the id is generated at the write path instead
        // (BrandsController.AddFuel), so the second row is accepted. It is not a cosmetic duplicate: it
        // becomes a second offer, the storefront lists one fuel at two prices, and the voucher importer
        // resolves the fuel by name with FirstOrDefault (OkkoFuelClassifier/WogFuelClassifier), so a
        // real voucher can land on the twin that never received the imported pump price.
        //
        // This REPLACES the plain (station_id, name) index rather than sitting beside it — same key, so
        // nothing that used it loses a plan, and the only queries that filter on both columns
        // (BrandsController's duplicate check, KloVoucherParser) read live rows anyway.
        builder.HasIndex(e => new { e.StationId, e.Name })
            .IsUnique()
            .HasFilter("NOT is_deleted")
            .HasDatabaseName("ux_fuel_types_station_name");

        // The same guarantee, on the half of a fuel's identity that is not free text: one live row per
        // brand fuel code per network. Nullable on purpose, and therefore excluded from the index - a
        // fuel entered by hand has no code, and two such rows at one station must not collide with
        // each other over a value neither of them has.
        builder.HasIndex(e => new { e.StationId, e.BrandFuelCode })
            .IsUnique()
            .HasFilter("brand_fuel_code IS NOT NULL AND NOT is_deleted")
            .HasDatabaseName("ux_fuel_types_station_brand_fuel_code");

        // Natural key ("okko-95"), deliberately not a UUID. Bounded by CHECK rather than by a type
        // change - see StationConfiguration for why converting a natural key is not worth the
        // refactor, and what the CHECK buys instead.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fuel_types_id_length", "length(id) <= 100");
            t.HasCheckConstraint("ck_fuel_types_name_length", "length(name) <= 200");
            t.HasCheckConstraint("ck_fuel_types_station_id_length", "length(station_id) <= 100");

            // 50 for a brand's own code for a grade - "A-95", "Pulls 95", "SPBT", a QR product
            // code. Generous for what it is, bounded because it is indexed and unique per station.
            t.HasCheckConstraint("ck_fuel_types_brand_fuel_code_length", "brand_fuel_code IS NULL OR length(brand_fuel_code) <= 50");

            // A negative discount price means the customer is paid to take the fuel, which is not a
            // discount policy anyone has agreed to. The only guard today is the application-level
            // below-cost opt-in (BrandsController), which is a business decision rather than a
            // constraint, so a hand-written PUT or a future code path can still land here.
            t.HasCheckConstraint("ck_fuel_types_prices_non_negative", "base_price >= 0 AND discount_price >= 0");
        });
    }
}
