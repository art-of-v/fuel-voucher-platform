using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the five seeded OKKO fuels the codes OKKO publishes for them. Planning #349, step 6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the rows that exist in <em>every</em> environment, so unlike the 28 fuels an
    /// operator created there is nothing to guess here: each mapping was read off OKKO's own published
    /// list (<c>/api/uk/fuels</c>, in the payload the fetchers already parse) and checked against the
    /// grade it names. That is the difference from the backfill this deliberately does not do — no name
    /// is matched against anything.
    /// </para>
    /// <para>
    /// <b>Only OKKO.</b> WOG's five seeded fuels get nothing, because nothing has ever shown us WOG's
    /// vocabulary: inventing "A-95" for WOG would be a guess about a supplier's naming, and a guess
    /// here is an identity <c>ux_fuel_types_station_brand_fuel_code</c> would then enforce. They stay
    /// NULL and price by category until a WOG source is proven — see
    /// <c>scripts/fetch-upg-wog-prices.mjs</c>, which found UPG's list and found WOG's intermittently.
    /// </para>
    /// <para>
    /// <b>Targeted UPDATE by key, not a name match.</b> The five ids are seed ids — an operator-created
    /// fuel gets <c>Guid.CreateVersion7()</c>, so <c>okko-95</c> cannot be anything else — and
    /// <c>brand_fuel_code</c> is NULL on every row in every environment, so this cannot overwrite
    /// anyone's work. It is the one case where EF's scaffolded <c>UpdateData</c> is the right shape.
    /// </para>
    /// </remarks>
    public partial class SeedOkkoBrandFuelCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "brand_fuel_code",
                value: "A-95");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "brand_fuel_code",
                value: "DP");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "brand_fuel_code",
                value: "SPBT");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "brand_fuel_code",
                value: "Pulls 95");

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "brand_fuel_code",
                value: "Pulls Diesel");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "brand_fuel_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "brand_fuel_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "brand_fuel_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "brand_fuel_code",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "brand_fuel_code",
                value: null);
        }
    }
}
