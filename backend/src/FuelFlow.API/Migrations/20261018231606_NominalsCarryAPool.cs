using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    /// <summary>
        /// Gives every nominal the pool it belongs to, so the rehearsal catalogue may sell sizes the
        /// live one does not.
        /// </summary>
        /// <remarks>
        /// <b>The existing rows are all marked <c>real</c>, and that is a decision rather than a
        /// detail.</b> The column arrives with a default because a NOT NULL column needs one, and the
        /// rows already in the table were written by the 2026-10-18 backfill from every live volume -
        /// both pools collapsed into one set, because at that point a size was the fuel's. Which pool
        /// each row came from is not recoverable now: <c>fuel_offer_volumes</c>, the table that knew,
        /// was dropped in <c>DropFuelOfferVolumes</c> one release earlier. Marking them all live is
        /// the safe direction - the consequence is a rehearsal catalogue with no sizes of its own until
        /// the QA scripts seed it, and the alternative would publish rehearsal rows as live ones.
        /// </remarks>
        public partial class NominalsCarryAPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_fuel_nominals",
                table: "fuel_nominals");

            // Default 'real', not "": a "" that satisfies nothing is refused by the CHECK three
            // statements below, and this migration runs inside the container start-up that
            // MigrateDatabaseOnStartup rethrows from.
            migrationBuilder.AddColumn<string>(
                name: "data_kind",
                table: "fuel_nominals",
                type: "text",
                nullable: false,
                defaultValue: "real");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fuel_nominals",
                table: "fuel_nominals",
                columns: new[] { "fuel_type_id", "data_kind", "liters" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_nominals_data_kind",
                table: "fuel_nominals",
                sql: "data_kind IN ('real', 'qa')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_fuel_nominals",
                table: "fuel_nominals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_nominals_data_kind",
                table: "fuel_nominals");

            migrationBuilder.DropColumn(
                name: "data_kind",
                table: "fuel_nominals");

            migrationBuilder.AddPrimaryKey(
                name: "PK_fuel_nominals",
                table: "fuel_nominals",
                columns: new[] { "fuel_type_id", "liters" });
        }
    }
}
