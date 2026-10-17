using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the five seeded OKKO fuels the codes OKKO publishes for them. Planning #349, step 6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the rows that exist in <em>every</em> environment, so unlike the fuels an operator
    /// created there is nothing to guess here: each mapping was read off OKKO's own published list
    /// (<c>/api/uk/fuels</c>, in the payload the fetchers already parse) and checked against the grade
    /// it names. That is the difference from the backfill this deliberately does not do — no name is
    /// matched against anything.
    /// </para>
    /// <para>
    /// <b>Only OKKO.</b> WOG's five seeded fuels get nothing, because nothing has ever shown us WOG's
    /// vocabulary: inventing "A-95" for WOG would be a guess about a supplier's naming, and a guess here
    /// is an identity <c>ux_fuel_types_station_brand_fuel_code</c> would then enforce. They stay NULL and
    /// price by category until a WOG source is proven — see <c>scripts/fetch-upg-wog-prices.mjs</c>, which
    /// found UPG's list and found WOG's intermittently.
    /// </para>
    /// <para>
    /// <b>Targeted UPDATE by key, not a name match.</b> The five ids are seed ids — an operator-created
    /// fuel gets <c>Guid.CreateVersion7()</c>, so <c>okko-95</c> cannot be anything else — and
    /// <c>brand_fuel_code</c> is NULL on every row in every environment, so this overwrites nobody's
    /// work.
    /// </para>
    /// </remarks>
    public partial class SeedOkkoBrandFuelCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Written as SQL rather than EF's UpdateData, and not for taste.
            //
            // EF generates a data operation's SQL against the model recorded by the NEXT migration, not
            // against the current one. Two branches were changing the model at the same time, and the
            // migrations that landed after this one was cut (VoucherEvents,
            // CloseTheNullHoleInVoucherEventPairing) carry Designer snapshots with no
            // `brand_fuel_code` in them, because they were scaffolded before #1190 landed. Rolling the
            // schema back therefore failed before touching a row:
            //
            //     There is no property mapped to the column 'fuel_types.brand_fuel_code'
            //     which is used in a data operation.
            //
            // Raw SQL depends on nothing but the database, which is the same reason
            // `SeedCatalogueAsOffers` writes its inserts out by hand. The stale snapshots remain a
            // hazard for the next branch that scaffolds a `fuel_types` data operation; they belong to
            // whoever owns those migrations, and this one is not the place to edit them.
            migrationBuilder.Sql(
                """
                UPDATE fuel_types SET brand_fuel_code = 'A-95'         WHERE id = 'okko-95';
                UPDATE fuel_types SET brand_fuel_code = 'DP'           WHERE id = 'okko-dp';
                UPDATE fuel_types SET brand_fuel_code = 'Pulls 95'     WHERE id = 'okko-p95';
                UPDATE fuel_types SET brand_fuel_code = 'Pulls Diesel' WHERE id = 'okko-pulls-dp';
                UPDATE fuel_types SET brand_fuel_code = 'SPBT'         WHERE id = 'okko-gas';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The codes are cleared, not the rows: the same five rows in reverse, and idempotent whether
            // or not the Up ever ran.
            migrationBuilder.Sql(
                """
                UPDATE fuel_types SET brand_fuel_code = NULL
                WHERE id IN ('okko-95', 'okko-dp', 'okko-p95', 'okko-pulls-dp', 'okko-gas');
                """);
        }
    }
}
