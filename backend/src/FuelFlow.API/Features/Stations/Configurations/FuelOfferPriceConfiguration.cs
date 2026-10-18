using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

/// <summary>
/// <c>fuel_offer_prices</c> - the append-only record of what each offer was priced at, and when.
/// </summary>
/// <remarks>
/// <para>
/// No query filter, unlike <see cref="FuelOfferConfiguration"/>: a history row has no
/// <c>is_deleted</c> because it is never deleted, and the point of it is that it stays readable after
/// the offer is archived - an archived offer is out of the catalogue, not out of the record.
/// </para>
/// <para>
/// The indexes follow the two questions asked of this table. <c>(offer_id, effective_from)</c> serves
/// "what did this fuel cost, and when did each price start" - the chronological read of one offer's
/// history. <c>(fuel_type_id, effective_from)</c> serves the cross-offer one: a fuel has an offer per
/// pool, and "what happened to A95 across both pools this week" joins on the fuel rather than the key.
/// </para>
/// </remarks>
internal sealed class FuelOfferPriceConfiguration : IEntityTypeConfiguration<FuelOfferPrice>
{
    public void Configure(EntityTypeBuilder<FuelOfferPrice> builder)
    {
        builder.ToTable("fuel_offer_prices");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("uuid");
        builder.Property(e => e.OfferId).HasColumnName("offer_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.BrandId).HasColumnName("brand_id").HasColumnType("text").IsRequired();
        builder.Property(e => e.FuelTypeId).HasColumnName("fuel_type_id").HasColumnType("text").IsRequired();

        // Same six columns, same scale, as the offer they are copied from: a history row that stored a
        // price at a different precision than the live price would make "the same number" a judgement
        // call rather than an equality.
        builder.Property(e => e.SupplierPricePerLiter).HasColumnName("supplier_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MarginUahPerLiter).HasColumnName("margin_uah_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MarginPercent).HasColumnName("margin_percent").HasColumnType("numeric(10,4)");
        builder.Property(e => e.FinalPricePerLiter).HasColumnName("final_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.PumpPricePerLiter).HasColumnName("pump_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MinDiscountPerLiter).HasColumnName("min_discount_per_liter").HasColumnType("numeric(10,4)");

        builder.Property(e => e.DataKind).HasColumnName("data_kind").HasMaxLength(10).IsRequired();

        builder.Property(e => e.EffectiveFromUtc).HasColumnName("effective_from_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.RecordedAtUtc).HasColumnName("recorded_at_utc").HasColumnType("timestamptz").IsRequired();

        // A bare uuid, no foreign key - see FuelOfferPrice.ChangedByUserId. The column is indexed all
        // the same, because "everything this person repriced" is a question worth being able to ask.
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid");

        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(32).IsRequired();

        builder.HasIndex(e => e.ChangedByUserId);

        builder.HasIndex(e => new { e.OfferId, e.EffectiveFromUtc })
            .HasDatabaseName("ix_fuel_offer_prices_offer_effective");
        builder.HasIndex(e => new { e.FuelTypeId, e.EffectiveFromUtc })
            .HasDatabaseName("ix_fuel_offer_prices_fuel_effective");

        builder.HasOne(e => e.Offer)
            .WithMany(o => o.Prices)
            .HasForeignKey(e => e.OfferId)
            // Restrict, like every other relation here. A price is a record of something sold, so the
            // offer it was sold under must stay readable - which is why removing the rehearsal pool
            // removes its history first.
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            // The same value sets and sign constraints the offer itself carries, and deliberately so: a
            // history row is written by the same five paths as the offer, and a CHECK is the one thing
            // that holds for a row written by a migration, a seed or a future caller alike.
            t.HasCheckConstraint("ck_fuel_offer_prices_data_kind", "data_kind IN ('real', 'qa')");
            t.HasCheckConstraint("ck_fuel_offer_prices_reason",
                "reason IN ('operator', 'pump-import', 'catalogue-import', 'batch-cost', 'exchange-release', 'backfill')");
            t.HasCheckConstraint("ck_fuel_offer_prices_margin_signs",
                "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
            t.HasCheckConstraint("ck_fuel_offer_prices_prices_non_negative",
                "(final_price_per_liter IS NULL OR final_price_per_liter >= 0) AND " +
                "(pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND " +
                "(min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");

            // A history row with no offer has no fuel to price and cannot be read back: refuse it at the
            // database rather than in a remark that says "should not happen".
            t.HasCheckConstraint("ck_fuel_offer_prices_offer_id_length", "length(offer_id) <= 103");
            t.HasCheckConstraint("ck_fuel_offer_prices_fuel_type_id_length", "length(fuel_type_id) <= 100");
            t.HasCheckConstraint("ck_fuel_offer_prices_brand_id_length", "length(brand_id) <= 100");
        });
    }
}