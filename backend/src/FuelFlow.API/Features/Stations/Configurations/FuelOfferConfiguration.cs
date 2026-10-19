using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

/// <summary>
/// <c>fuel_offers</c> — the per-litre facts of one commercial offer, stored once. Planning #318,
/// finding F-33: these were duplicated across every volume row of the same offer.
/// </summary>
internal sealed class FuelOfferConfiguration : IEntityTypeConfiguration<FuelOffer>
{
    public void Configure(EntityTypeBuilder<FuelOffer> builder)
    {
        builder.ToTable("fuel_offers");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("text").IsRequired();
        builder.Property(e => e.BrandId).HasColumnName("brand_id").HasColumnType("uuid").IsRequired();
        builder.Property(e => e.FuelTypeId).HasColumnName("fuel_type_id").HasColumnType("text").IsRequired();

        builder.Property(e => e.SupplierPricePerLiter).HasColumnName("supplier_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MarginPercent).HasColumnName("margin_percent").HasColumnType("numeric(10,4)");
        builder.Property(e => e.PumpPricePerLiter).HasColumnName("pump_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MinDiscountPerLiter).HasColumnName("min_discount_per_liter").HasColumnType("numeric(10,4)");

        builder.Property(e => e.PriceUpdatedAt).HasColumnName("price_updated_at");
        builder.Property(e => e.PriceUpdatedByUserId).HasColumnName("price_updated_by_user_id");

        builder.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamptz");
        builder.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamptz");

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // NOT folded into the query filter on purpose - see FuelOffer.DataKind. A filter would also
        // hide the offer from the admin screens that have to manage it.
        builder.Property(e => e.DataKind)
            .HasColumnName("data_kind")
            .HasMaxLength(10)
            .IsRequired();

        // Same filter as FuelPackage.IsDeleted has. An offer whose volumes are all archived is out of the
        // catalogue; one that still sells another size stays live regardless of this flag, which is
        // why the flag is maintained by the write paths rather than derived on read. Anything that
        // needs the archived offer asks with IgnoreQueryFilters().
        builder.HasQueryFilter(e => !e.IsDeleted);

        // The storefront reads "the offers of this pool, not archived", and the admin reads by
        // Brand. Leading with is_deleted matches fuel_packages' own index.
        builder.HasIndex(e => new { e.IsDeleted, e.DataKind });
        builder.HasIndex(e => e.BrandId);
        builder.HasIndex(e => new { e.BrandId, e.FuelTypeId });

        // The way to the fuel, so a read path can take the margin from there rather than from a copy
        // of it on the offer. Restrict, like every other relation here.
        builder.HasOne(e => e.FuelType)
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The natural key, stated once more by the database. This is the guarantee that a second
        // offer cannot appear for the same (Brand, fuel, pool) - the precondition for grouping the
        // old rows into one price.
        builder.HasIndex(e => new { e.BrandId, e.FuelTypeId, e.DataKind })
            .IsUnique()
            .HasDatabaseName("ix_fuel_offers_brand_fuel_pool");

        builder.ToTable(t =>
        {
            // 103, derived rather than guessed: the key is the fuel type id - bounded at 100 by
            // ck_fuel_types_id_length - plus the '-qa' suffix. OfferKeyTests states the same arithmetic,
            // so a tightened column bound cannot silently start rejecting a legal offer at write time.
            t.HasCheckConstraint("ck_fuel_offers_id_length", "length(id) <= 103");
            t.HasCheckConstraint("ck_fuel_offers_fuel_type_id_length", "length(fuel_type_id) <= 100");

            // The value set DataKinds.All documents.
            t.HasCheckConstraint("ck_fuel_offers_data_kind", "data_kind IN ('real', 'qa')");

            // The sign constraints on the columns that are money here. A negative figure is not a
            // cheap mistake to make: the dashboard and reconciliation both multiply this straight
            // into revenue. The margin is not among them - it moved to the fuel in
            // 20261018181305_MarginLivesOnTheFuel, and the constraint that pinned it followed.
            t.HasCheckConstraint("ck_fuel_offers_prices_non_negative",
                "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND " +
                "(pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND " +
                "(min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        });
    }
}
