using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Drops <c>fuel_offers.final_price_per_liter</c>: the customer price is computed from the offer's
    /// knobs at read time instead of being stored beside them. Planning D-07 step 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What the column was.</b> <c>min(supplier + margin, pump − minDiscount)</c> — see
    /// <c>FuelPricing.FinalPerLiter</c> — written down next to the three columns it is computed from. Four
    /// of the five write paths computed it exactly that way before storing it; the fifth,
    /// <c>CreatePackageCommandHandler</c>, took it from the request body and wrote whatever the caller
    /// sent, unvalidated against the knobs above it. That is the whole risk of dropping it, and it is
    /// why this migration guards rather than assumes.
    /// </para>
    /// <para>
    /// <b>The guard.</b> Measured on production 2026-10-17: <b>0</b> of 25 live offers disagreed with the
    /// formula. If that is ever untrue the migration refuses, and says which ids — because a disagreement
    /// means an override somebody set by hand, and silently discarding it would drop a price the
    /// storefront had been serving without anyone deciding to. Same guard shape as
    /// <c>SplitFuelPackagesIntoOffers</c>, for the same reason: a destructive step over money rows states
    /// what it checked before it changes anything.
    /// </para>
    /// <para>
    /// <b>Not touched: <c>fuel_offer_prices.final_price_per_liter</c>.</b> The history keeps its own copy
    /// of the figure, recorded at the moment it was true. That is the point of the history — a number that
    /// can still be read for a date — and the two columns share a name and nothing else.
    /// </para>
    /// <para>
    /// <b>The CHECK is replaced, not dropped.</b> <c>ck_fuel_offers_prices_non_negative</c> existed to keep
    /// three money columns non-negative, one of which was this one. It now covers the supplier cost too,
    /// which it did not: a negative cost was reachable until this migration, and a negative cost makes
    /// every price derived from it negative as well.
    /// </para>
    /// </remarks>
    public partial class DeriveTheOfferFinalPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_prices_non_negative",
                table: "fuel_offers");

            // Before the drop, while the column is still readable. Archived offers included: they are not
            // sellable, but their price is still a fact somebody might read in a report.
            //
            // The CASE mirrors FuelPricing.FinalPerLiterOrNull exactly, nullability included: no cost or no
            // margin means "not priced", and a stored NULL is the correct value for that - so an offer with
            // a NULL final price must not be compared against an arithmetic that pretends the missing knob
            // was zero. The 1000000000 is the "no ceiling recorded" sentinel: when pump_price_per_liter is
            // NULL the formula returns cost + profit outright, and LEAST against a value no real pump
            // reaches produces the same number.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    drifted text;
                BEGIN
                    SELECT string_agg(o.id, ', ' ORDER BY o.id) INTO drifted
                    FROM fuel_offers o
                    WHERE o.final_price_per_liter IS DISTINCT FROM (
                        CASE
                            WHEN o.supplier_price_per_liter IS NULL OR o.margin_uah_per_liter IS NULL THEN NULL
                            ELSE LEAST(
                                o.supplier_price_per_liter + o.margin_uah_per_liter,
                                COALESCE(o.pump_price_per_liter, 1000000000)
                                    - COALESCE(o.min_discount_per_liter, 0))
                        END);

                    IF drifted IS NOT NULL THEN
                        RAISE EXCEPTION
                            'final_price_per_liter disagrees with min(supplier + margin, pump - minDiscount) '
                            'on these offers: % . The column is dropped because every write path computed it that '
                            'way; a disagreement means an override set by hand, which is a decision to make rather '
                            'than to discard silently. Re-price them, or leave the column in place.', drifted;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "final_price_per_liter",
                table: "fuel_offers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_prices_non_negative",
                table: "fuel_offers",
                sql: "(supplier_price_per_liter IS NULL OR supplier_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_offers_prices_non_negative",
                table: "fuel_offers");

            migrationBuilder.AddColumn<decimal>(
                name: "final_price_per_liter",
                table: "fuel_offers",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "final_price_per_liter",
                value: 51m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "final_price_per_liter",
                value: 52m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "final_price_per_liter",
                value: 27m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "final_price_per_liter",
                value: 56m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "final_price_per_liter",
                value: 55m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "final_price_per_liter",
                value: 61m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-95",
                column: "final_price_per_liter",
                value: 52m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                column: "final_price_per_liter",
                value: 53m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-dp",
                column: "final_price_per_liter",
                value: 53m);

            migrationBuilder.UpdateData(
                table: "fuel_offers",
                keyColumn: "id",
                keyValue: "wog-gas",
                column: "final_price_per_liter",
                value: 27m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_offers_prices_non_negative",
                table: "fuel_offers",
                sql: "(final_price_per_liter IS NULL OR final_price_per_liter >= 0) AND (pump_price_per_liter IS NULL OR pump_price_per_liter >= 0) AND (min_discount_per_liter IS NULL OR min_discount_per_liter >= 0)");
        }
    }
}
