using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

/// <summary>
/// <c>fuel_price_history</c> - the append-only record of what each fuel was priced at, and when.
/// </summary>
internal sealed class FuelPriceHistoryConfiguration : IEntityTypeConfiguration<FuelPriceHistory>
{
    public void Configure(EntityTypeBuilder<FuelPriceHistory> builder)
    {
        builder.ToTable("fuel_price_history");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").HasColumnType("uuid");
        builder.Property(e => e.BrandId).HasColumnName("brand_id").HasColumnType("uuid").IsRequired();
           builder.Property(e => e.FuelTypeId).HasColumnName("fuel_type_id").HasColumnType("uuid")
               .IsRequired();

        // Same six columns, same scale, as the fuel they are copied from: a history row that stored
        // a price at a different precision than the live price would make "the same number" a
        // judgement call rather than an equality.
        builder.Property(e => e.SupplierPricePerLiter).HasColumnName("supplier_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MarginUahPerLiter).HasColumnName("margin_uah_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MarginPercent).HasColumnName("margin_percent").HasColumnType("numeric(10,4)");
        builder.Property(e => e.FinalPricePerLiter).HasColumnName("final_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.PumpPricePerLiter).HasColumnName("pump_price_per_liter").HasColumnType("numeric(10,4)");
        builder.Property(e => e.MinDiscountPerLiter).HasColumnName("min_discount_per_liter").HasColumnType("numeric(10,4)");

        builder.Property(e => e.DataKind).HasColumnName("data_kind").HasMaxLength(10).IsRequired();

        builder.Property(e => e.EffectiveFromUtc).HasColumnName("effective_from_utc").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.RecordedAtUtc).HasColumnName("recorded_at_utc").HasColumnType("timestamptz").IsRequired();

        // A bare uuid, no foreign key - see FuelPriceHistory.ChangedByUserId. The column is indexed all
        // the same, because "everything this person repriced" is a question worth being able to ask.
        builder.Property(e => e.ChangedByUserId).HasColumnName("changed_by_user_id").HasColumnType("uuid");

        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(32).IsRequired();

        builder.HasIndex(e => e.ChangedByUserId);

        // The foreign key below needs an index to lead with, and this is the only column in the
        // table that does not already have one: fuel_type_id leads ix_fuel_price_history_fuel_effective.
        builder.HasIndex(e => e.BrandId).HasDatabaseName("ix_fuel_price_history_brand_id");

        builder.HasIndex(e => new { e.FuelTypeId, e.EffectiveFromUtc })
            .HasDatabaseName("ix_fuel_price_history_fuel_effective");
        // Two constraints that used to be comments.
        //
        // On the fuel: the argument for leaving this unconstrained was that "a fuel is never
        // deleted, it is archived, and an archived fuel keeps its whole price history readable".
        // That argument is about archiving; RESTRICT is about deleting, and the two do not
        // conflict. Archiving still keeps the history - the constraint only stops anything from
        // removing the fuel underneath it. On a price history, losing rows quietly is worse than
        // failing loudly, which is why this is RESTRICT rather than the CASCADE fuel_nominals
        // carries.
        //
        // On the brand: same reasoning as fuel_types and brand_stations, both RESTRICT.
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FuelTypeEntity>()
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            // The same value sets and sign constraints the fuel itself carries, and deliberately so: a
            // history row is written by the same five paths as the fuel's price, and a CHECK is the one
            // thing that holds for a row written by a migration, a seed or a future caller alike.
            t.HasCheckConstraint("ck_fuel_price_history_data_kind", "data_kind IN ('real', 'qa')");
            t.HasCheckConstraint("ck_fuel_price_history_reason",
                "reason IN ('operator', 'pump-import', 'catalogue-import', 'batch-cost', 'exchange-release', 'backfill')");
            t.HasCheckConstraint("ck_fuel_price_history_margin_signs",
                "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
            t.HasCheckConstraint("ck_fuel_price_history_prices_non_negative",
                "(final_price_per_liter IS NULL OR final_price_per_liter >= 0) AND " +
                "(pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND " +
                "(min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");

            });
    }
}
