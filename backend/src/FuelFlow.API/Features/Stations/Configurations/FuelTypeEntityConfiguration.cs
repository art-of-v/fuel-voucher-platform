using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Features.Brands.Configurations;

internal sealed class FuelTypeEntityConfiguration : IEntityTypeConfiguration<FuelTypeEntity>
{
    public void Configure(EntityTypeBuilder<FuelTypeEntity> builder)
    {
        builder.ToTable("fuel_types");

        builder.HasKey(e => e.Id);

        // The key a row that names both a fuel and a brand is checked against. A foreign key can
        // name a row by one column or by a set of them, and a set of them can say what a set of
        // names cannot: that the fuel this row names and the brand this row names are the same
        // fact. The four tables that carry a brand as a name are checked against this.
        builder.HasAlternateKey(e => new { e.Id, e.BrandId });

        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted").IsRequired();
        builder.Property(e => e.DeletedAtUtc).HasColumnName("deleted_at_utc");

        // fuel_vouchers.fuel_type_id cascades from here, so archiving rather than removing is what
        // keeps paid-for vouchers in existence (#217).
        builder.HasQueryFilter(e => !e.IsDeleted);

        // Restrict, for the same reason brand_stations carries one: a fuel is archived through
        // IsDeleted, and a DELETE arriving from anywhere else - including a hand-written psql
        // session - must fail loudly rather than take every grade of a network with it.
        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(e => e.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        // The id is a uuid since planning D-21: the brand's own code for the grade lives in
        // brand_fuel_code, and the id is an opaque handle the catalogue no longer spells. The
        // property is a Guid and the column is a uuid, so there is nothing left to convert.
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("uuid");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            // varchar(200), the bound ck_fuel_types_name_length already states.
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.BrandId)
            .HasColumnName("brand_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.BrandFuelCode)
            .HasColumnName("brand_fuel_code")
            // varchar(50), the bound ck_fuel_types_brand_fuel_code_length already states.
            .HasMaxLength(50);
        // Snake case like every other column here. Left unmapped, EF would scaffold
        // "MarginUahPerLiter" — a second spelling of one column in the same table, invisible until a
        // hand-written migration or a query names the other one.
        // numeric(10,4), the same width every other per-litre money column in the schema uses.
        // It was an unconstrained `numeric` here while fuel_offer_prices carried the same field at
        // numeric(10,4) - two spellings of one number, and an unbounded one on the table that is
        // now the single source of truth for it.
        builder.Property(e => e.MarginUahPerLiter)
            .HasColumnName("margin_uah_per_liter")
            .HasColumnType("numeric(10,4)")
            .HasComment("What we add per litre on top of what we pay. NULL means the margin is not "
                + "stated, and a fuel with no stated cost OR no stated margin has no price of its "
                + "own: it is shown at the column price, marked 'not in stock', and it cannot be sold.");

        // The four pricing knobs, moved off fuel_offers on 2026-10-19. Same naming discipline as
        // the margin above: one column, one spelling - and the same numeric(10,4) they had on the
        // offer, so the copy across is a value move and not a precision change. A history row that
        // stored a price at a different scale than the live price would make "the same number" a
        // judgement call rather than an equality.
        builder.Property(e => e.SupplierPricePerLiter)
            .HasColumnName("supplier_price_per_liter")
            .HasColumnType("numeric(10,4)")
            .HasComment("What WE pay the network per litre. NULL means the cost is not stated: the "
                + "fuel is unpriced, and the below-cost guard has nothing to compare against.");

        builder.Property(e => e.PumpPricePerLiter)
            .HasColumnName("pump_price_per_liter")
            .HasColumnType("numeric(10,4)")
            .HasComment("The COLUMN price - what the driver pays at the pump. It is a reference, not "
                + "a term of our price: our price is the cost plus the margin, and the column price "
                + "can sit below what we charge. It is the price the customer sees struck through "
                + "when the column price sits above what we charge.");

        builder.Property(e => e.MinDiscountPerLiter)
            .HasColumnName("min_discount_per_liter")
            .HasColumnType("numeric(10,4)")
            .HasComment("The smallest discount we give the customer per litre. RETIRED: the price "
                + "is computed as cost + margin, and the column price beside it is a reference, so "
                + "this column no longer takes part in it. It stays until the migration that drops "
                + "it.");

        builder.Property(e => e.MarginPercent)
            .HasColumnName("margin_percent")
            .HasColumnType("numeric(10,4)")
            .HasComment("The margin as a percentage, recorded for the operator's card. The price is "
                + "computed from the ABSOLUTE margin (margin_uah_per_liter); this column does not "
                + "feed the formula.");

        builder.Property(e => e.PriceUpdatedAt)
            .HasColumnName("price_updated_at")
            .HasColumnType("timestamp with time zone");

        // price_updated_by_user_id stays a bare uuid, like every other actor column on a row that
        // records who did something: the record has to survive the account. A key here would
        // either wedge the job that deletes a user or force it to delete the price history -
        // and the price history is the point (planning #307, TARGET_SCHEMA §G).
        builder.Property(e => e.PriceUpdatedByUserId)
            .HasColumnName("price_updated_by_user_id")
            .HasColumnType("uuid");

        builder.Property(e => e.BasePrice)
            .HasColumnName("base_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired(false)
            .HasComment("The list price of the fuel per litre - the price before our discount. It "
                + "is the reference the customer's saving is measured against. RETIRED: the "
                + "reference is the COLUMN price next door, and the saving is computed from it. "
                + "The column stays until the migration that drops it.");

        builder.Property(e => e.DiscountPrice)
            .HasColumnName("discount_price")
            .HasColumnType("numeric(12,2)")
            .IsRequired(false)
            .HasComment("What the customer pays per litre. NULL means it is not set: the grade is "
                + "in the catalogue and nobody has priced it yet. A price of zero is a price, and "
                + "a price of zero means the customer takes the fuel for nothing - so the unset "
                + "state is NULL and never 0.");

        builder.Property(e => e.AllowBelowCost)
            .HasColumnName("allow_below_cost")
            .HasDefaultValue(false)
            .IsRequired()
            .HasComment("Whether this fuel may be sold below what we pay. A manager's opt-in, per "
                + "fuel, for a deliberate loss leader - the write path refuses such a sale without it.");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The default used to be '-infinity', and it was not chosen: it is what Npgsql writes for
        // DateTime.MinValue, which is what EF's convention put into the migrations that created this
        // column. -infinity sorts before every real timestamp and serialises badly, and it is a value
        // no application means.
        //
        // now() is the honest fallback for a raw INSERT that does not state the timestamp, and
        // ValueGeneratedNever keeps the application in charge: it still sends its own value on every
        // insert, exactly as it did before, so nothing about the application's behaviour changes. The
        // default is a safety net for SQL that does not come from EF.
        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .ValueGeneratedNever()
            .IsRequired();

        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.BrandId);

        // (brand_id, name), unique and partial on NOT is_deleted. A natural key used to give "one
        // fuel per name per Brand" for free — the id was 'okko-95', so a second A-95 at OKKO collided
        // on the primary key — and the id is generated at the write path instead
        // (BrandsController.AddFuel), so the second row is accepted. It is not a cosmetic duplicate: it
        // becomes a second offer, the storefront lists one fuel at two prices, and the voucher importer
        // resolves the fuel by name with FirstOrDefault (OkkoFuelClassifier/WogFuelClassifier), so a
        // real voucher can land on the twin that never received the imported pump price.
        //
        // This REPLACES the plain (brand_id, name) index rather than sitting beside it — same key, so
        // nothing that used it loses a plan, and the only queries that filter on both columns
        // (BrandsController's duplicate check, KloVoucherParser) read live rows anyway.
        builder.HasIndex(e => new { e.BrandId, e.Name })
            .IsUnique()
            .HasFilter("NOT is_deleted")
            .HasDatabaseName("ux_fuel_types_brand_name");

        // The same guarantee, on the half of a fuel's identity that is not free text: one live row per
        // brand fuel code per network. Nullable on purpose, and therefore excluded from the index - a
        // fuel entered by hand has no code, and two such rows at one Brand must not collide with
        // each other over a value neither of them has.
        builder.HasIndex(e => new { e.BrandId, e.BrandFuelCode })
            .IsUnique()
            .HasFilter("brand_fuel_code IS NOT NULL AND NOT is_deleted")
            .HasDatabaseName("ux_fuel_types_brand_brand_fuel_code");

        // The id is a generated uuid, not a natural key - it is 'okko-95' ONLY in the rows
        // `20260618224553_AddStationCatalog` seeded, which is every row a freshly migrated
        // database has and none of the 28 on production, where every fuel an operator added has
        // carried a `Guid.CreateVersion7()` since. Planning #349 asks for one documented
        // convention; the owner chose uuid on 2026-10-19, and D-21's second slice makes the column
        // one. A fuel's NAME is still what every importer resolves a PDF row by, which is why
        // `brand_fuel_code` exists beside the id - not a readable id.
        builder.ToTable(t =>
        {
                t.HasCheckConstraint("ck_fuel_types_name_length", "length(name) <= 200");

            // The switch that says "this fuel may be sold below cost" means something only when
            // there is a cost to be below. Without one it is a switch that disarms the guard for
            // nothing - and it becomes a live one the moment a cost is recorded, for every fuel
            // that carries it. So the rule is in the schema: the switch can only be on for a
            // fuel whose cost is known.
            t.HasCheckConstraint("ck_fuel_types_allow_below_cost_needs_a_cost",
                "allow_below_cost = false OR supplier_price_per_liter IS NOT NULL");

            // 50 for a brand's own code for a grade - "A-95", "Pulls 95", "SPBT", a QR product
            // code. Generous for what it is, bounded because it is indexed and unique per Brand.
            t.HasCheckConstraint("ck_fuel_types_brand_fuel_code_length", "brand_fuel_code IS NULL OR length(brand_fuel_code) <= 50");

            // A price of zero is a price, and a price of zero means the customer takes the fuel
            // for nothing. So the unset state is NULL - which is what the cost beside it says - and
            // zero is not a price anybody may write.
            //
            // This is the business's call (2026-10-21): a grade the network lists but nobody has
            // priced yet has NO customer price, and the column says so by being empty. The
            // catalogue import leaves exactly that behind - the pump is published, no cost and no
            // margin are set - and a grade with no price is not on the shelf: its computed price
            // is null and the storefront does not list a package with no price.
            t.HasCheckConstraint("ck_fuel_types_prices_non_negative",
                "base_price >= 0 AND (discount_price IS NULL OR discount_price > 0)");

            // The four knobs the fuel took off the offer on 2026-10-19 keep the constraints the offer
            // carried. A negative cost makes every price computed from it negative as well, and a
            // negative discount is the customer being paid to take the fuel.
            t.HasCheckConstraint("ck_fuel_types_margin_signs",
                "margin_uah_per_liter IS NULL OR margin_uah_per_liter >= 0");
            t.HasCheckConstraint("ck_fuel_types_pricing_knobs_non_negative",
                "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND " +
                "(pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND " +
                "(min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        });
    }
}
