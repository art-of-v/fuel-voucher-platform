using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Replaces <c>fuel_offer_volumes.package_price</c> with a nullable
    /// <c>price_override_uah</c>. Planning D-07 step 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the column was.</b> A stored copy of <c>round(final_price_per_liter × liters, 2)</c> — and
    /// by 2026-10-17 the offer itself carried no stored final price to copy, so the column had become the
    /// only place the figure existed. Two admin endpoints could write any value into it with nothing
    /// checking it against the offer, and five other write paths then overwrote it back to the arithmetic:
    /// a duplicate that could disagree, and an override that a single pump import could erase.
    /// </para>
    /// <para>
    /// <b>What replaces it, and why the two columns behave so differently.</b> Null means "price this
    /// nominal from the offer's rate", which is what every row on production was: 122 live volumes, 0
    /// disagreeing with the arithmetic. A number means an operator priced this size on purpose, and it now
    /// survives a reprice. Which one is in force is resolved at read time by
    /// <c>FuelPricing.PackagePrice</c> — one function, used by the shelf, the quote and the charge alike,
    /// so a displayed price and a charged price cannot drift.
    /// </para>
    /// <para>
    /// <b>Rows that disagree are carried over, not dropped.</b> A volume whose stored total did not match
    /// its offer was being sold at the stored number — that is what the customer saw and what checkout
    /// charged — so it becomes an explicit override rather than silently snapping to the arithmetic. On
    /// production that is 0 rows, and the count is printed by the backfill's own report so a deployment
    /// where it is not zero says so.
    /// </para>
    /// <para>
    /// <b>Order matters.</b> The new column is added and populated <em>before</em> the old one is dropped, so
    /// there is no window in which a volume has no price at all — not even one a concurrent write could see.
    /// </para>
    /// </remarks>
    public partial class PackagePriceOverrideOnNominals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_volumes_package_price_non_negative",
                table: "fuel_offer_volumes");

            migrationBuilder.AddColumn<decimal>(
                name: "price_override_uah",
                table: "fuel_offer_volumes",
                type: "numeric(12,2)",
                nullable: true);

            // The one statement that decides anything. Everything that agreed with its offer keeps null —
            // the ordinary case — and everything that disagreed keeps its number as a deliberate override.
            //
            // The rate mirrors FuelPricing.FinalPerLiterOrNull, including its nullability: an offer with
            // no supplier cost or no margin has no rate, so nothing can be compared against it and such a
            // volume keeps its stored total as an override rather than being judged against a fabricated
            // price. Archived volumes are included on purpose — they are not sellable, but they are
            // history and their numbers should not change under it.
            migrationBuilder.Sql("""
                UPDATE fuel_offer_volumes v
                SET price_override_uah = v.package_price
                FROM fuel_offers o
                WHERE o.id = v.offer_id
                  AND v.package_price IS DISTINCT FROM (
                        CASE
                            WHEN o.supplier_price_per_liter IS NULL OR o.margin_uah_per_liter IS NULL
                                THEN v.package_price
                            ELSE round(
                                LEAST(
                                    o.supplier_price_per_liter + o.margin_uah_per_liter,
                                    COALESCE(o.pump_price_per_liter, 1000000000)
                                        - COALESCE(o.min_discount_per_liter, 0))
                                * v.liters,
                                2)
                        END);
                """);

            // How many rows are exceptions rather than arithmetic. On production: 0 of 122 live, so every
            // volume after this migration follows its offer.
            migrationBuilder.Sql("""
                DO $$
                DECLARE carried integer;
                BEGIN
                    SELECT count(*) INTO carried FROM fuel_offer_volumes WHERE price_override_uah IS NOT NULL;
                    IF carried > 0 THEN
                        RAISE NOTICE
                            'fuel_offer_volumes: % row(s) carried a price their offer does not produce and now hold '
                            'it as an explicit override. They were being sold at that number before this migration.',
                            carried;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "package_price",
                table: "fuel_offer_volumes");

            // `ck_fuel_offer_volumes_price_override_non_negative` is added below, where EF scaffolded it:
            // the constraint belongs to the model, so the generated statement is the one that matches it.

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                column: "price_override_uah",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                column: "price_override_uah",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_volumes_price_override_non_negative",
                table: "fuel_offer_volumes",
                sql: "price_override_uah IS NULL OR price_override_uah >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offer_volumes_price_override_non_negative",
                table: "fuel_offer_volumes");

            migrationBuilder.AddColumn<decimal>(
                name: "package_price",
                table: "fuel_offer_volumes",
                type: "numeric(12,2)",
                nullable: false,
                // Every row is filled before the NOT NULL lands, below.
                defaultValue: 0m);

            // Roll back means every volume carries a stored total again, because the column was NOT NULL.
            // An override comes back as itself; a derived nominal is recomputed from its offer, which is
            // what it held before this migration went up — so a rolled-back database holds the prices it
            // held, not zeros.
            migrationBuilder.Sql("""
                UPDATE fuel_offer_volumes v
                SET package_price = COALESCE(
                        v.price_override_uah,
                        round(
                            LEAST(
                                COALESCE(o.supplier_price_per_liter, 0) + COALESCE(o.margin_uah_per_liter, 0),
                                COALESCE(o.pump_price_per_liter, 1000000000)
                                    - COALESCE(o.min_discount_per_liter, 0))
                            * v.liters,
                            2))
                FROM fuel_offers o
                WHERE o.id = v.offer_id;
                """);

            migrationBuilder.DropColumn(
                name: "price_override_uah",
                table: "fuel_offer_volumes");

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-10",
                column: "package_price",
                value: 510m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-20",
                column: "package_price",
                value: 1020m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-95-50",
                column: "package_price",
                value: 2550m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                column: "package_price",
                value: 520m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                column: "package_price",
                value: 104m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                column: "package_price",
                value: 1040m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                column: "package_price",
                value: 156m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                column: "package_price",
                value: 2600m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                column: "package_price",
                value: 270m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                column: "package_price",
                value: 540m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                column: "package_price",
                value: 1350m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                column: "package_price",
                value: 560m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                column: "package_price",
                value: 1120m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                column: "package_price",
                value: 2800m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                column: "package_price",
                value: 550m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                column: "package_price",
                value: 1100m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                column: "package_price",
                value: 2750m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-10",
                column: "package_price",
                value: 610m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-20",
                column: "package_price",
                value: 1220m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-100-50",
                column: "package_price",
                value: 3050m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-10",
                column: "package_price",
                value: 520m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-20",
                column: "package_price",
                value: 1040m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-50",
                column: "package_price",
                value: 2600m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                column: "package_price",
                value: 530m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                column: "package_price",
                value: 1060m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                column: "package_price",
                value: 2650m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                column: "package_price",
                value: 530m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                column: "package_price",
                value: 1060m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                column: "package_price",
                value: 2650m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                column: "package_price",
                value: 270m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                column: "package_price",
                value: 540m);

            migrationBuilder.UpdateData(
                table: "fuel_offer_volumes",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                column: "package_price",
                value: 1350m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offer_volumes_package_price_non_negative",
                table: "fuel_offer_volumes",
                sql: "package_price >= 0");
        }
    }
}
