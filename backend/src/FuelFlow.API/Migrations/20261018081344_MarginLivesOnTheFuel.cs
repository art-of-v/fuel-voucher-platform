using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Moves the margin onto the fuel itself.
    /// <para>
    /// A margin is a fact about a fuel, not about a sale of it — OKKO's diesel carries the same margin
    /// whether we hold it as stock, sell five litres or a hundred. On <c>fuel_offers</c> it was per
    /// (brand × fuel × pool), so there were two places to set one number and a price that could differ
    /// depending on which pool answered. Settled with the operator, 2026-10-17 (planning #360).
    /// </para>
    /// <para>
    /// The column is added and the data copied, but <b>no read path changes here</b>: the storefront
    /// and checkout still read the offer, and only the admin writes the new column. Dropping the old
    /// one is the following step, so the only thing this migration has to get right is the number.
    /// </para>
    /// <para>
    /// <b>Which offer wins, where a fuel had several.</b> The live pool first, then the most recently
    /// repriced. A rehearsal price read as a live margin would price a customer off the wrong pool —
    /// the mistake <c>data_kind</c> exists to prevent everywhere else in this schema.
    /// </para>
    /// </summary>
    public partial class MarginLivesOnTheFuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_types",
                type: "numeric",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-dp",
                column: "margin_uah_per_liter",
                value: null);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                column: "margin_uah_per_liter",
                value: null);
                // After the seed rows above, so a seeded fuel's margin lands from its offer rather
                // than from the scaffold's null.
                migrationBuilder.Sql(
                    """
                    UPDATE fuel_types AS f
                       SET margin_uah_per_liter = src.margin_uah_per_liter
                      FROM (
                            SELECT DISTINCT ON (fuel_type_id)
                                   fuel_type_id,
                                   margin_uah_per_liter
                              FROM fuel_offers
                             WHERE NOT is_deleted
                               AND margin_uah_per_liter IS NOT NULL
                             ORDER BY fuel_type_id,
                                      CASE WHEN data_kind = 'real' THEN 0 ELSE 1 END,
                                      updated_at_utc DESC
                           ) AS src
                     WHERE f.id = src.fuel_type_id
                       AND NOT f.is_deleted;
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "margin_uah_per_liter",
                table: "fuel_types");
        }
    }
}
