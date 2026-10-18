using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the offer a real relation to its fuel, so a read path can take the margin from
    /// <c>fuel_types.margin_uah_per_liter</c> — where a margin belongs — instead of from the copy on
    /// the offer. Planning #360, step 4b-1.
    /// <para>
    /// <b>The FK is the point and also the risk.</b> The column already existed and was populated; what
    /// is new is that the database will now <em>refuse</em> an offer pointing at no fuel. Checked
    /// against production before writing this: <b>0 orphan offers</b> across all 16. Had there been
    /// any, this migration would have failed on deploy rather than left the schema inconsistent, which
    /// is the correct way for it to fail — but it is the kind of failure that holds a release, so it is
    /// stated here rather than discovered.
    /// </para>
    /// <para>
    /// <c>RESTRICT</c>, like every other relation in this schema: an offer whose fuel is gone would
    /// price nothing, and removing a fuel with stock is the archive rule's job (#1240), not a cascade's.
    /// </para>
    /// </summary>
    public partial class OfferHasTheWayToItsFuel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_fuel_offers_fuel_type_id",
                table: "fuel_offers",
                column: "fuel_type_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_offers_fuel_types_fuel_type_id",
                table: "fuel_offers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_offers_fuel_types_fuel_type_id",
                table: "fuel_offers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_offers_fuel_type_id",
                table: "fuel_offers");
        }
    }
}
