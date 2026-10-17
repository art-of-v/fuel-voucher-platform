using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives <c>fuel_types</c> the network's own code for a grade, and makes it unique per network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a column, and why now.</b> A fuel's identity in this system used to be its
    /// <em>name</em>: the id was <c>'okko-95'</c>, so the name was the key, a second "А-95" at the same
    /// brand collided on the primary key, and the voucher importer resolved a fuel by name. The write
    /// path began generating the id instead, and the name stopped being anything the system agrees on.
    /// The code is the half of a fuel's identity that is not free text, and a network's own price
    /// sheet names every fuel it sells — so it is what a catalogue import can match on. Planning #349.
    /// </para>
    /// <para>
    /// <b>Nothing is backfilled, deliberately.</b> The obvious migration writes a code for every
    /// existing row by matching its name through <c>OkkoFuelClassifier</c>, and that is precisely the
    /// fragility this column exists to remove: the classifier maps a name to a <em>category</em>
    /// (A95, DieselPulls), not to a supplier's code, so every value it produced would be a guess. A
    /// wrong code is worse than no code — it is a wrong identity that a unique index then enforces —
    /// so the column starts empty and is filled by hand or by the import, where the network itself is
    /// the source. The ten seeded fuels are affected by this too, and stay NULL for the same reason.
    /// </para>
    /// <para>
    /// <b>Nullable, and excluded from the index for that reason.</b> A fuel entered by hand may have no
    /// code, and two such rows at one station must not collide over a value neither of them has.
    /// </para>
    /// </remarks>
    public partial class AddFuelTypeSupplierCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "supplier_code",
                table: "fuel_types",
                type: "text",
                nullable: true);

            // EF scaffolded ten `UpdateData(..., value: null)` statements here, because the catalogue
            // seed now carries a null `supplier_code`. They are no-ops on a database where the column
            // was just added and ten writes on one where it was not, and the reason the column is
            // empty is the class remark above - so they are removed rather than shipped.
            migrationBuilder.CreateIndex(
                name: "ux_fuel_types_station_supplier_code",
                table: "fuel_types",
                columns: new[] { "station_id", "supplier_code" },
                unique: true,
                filter: "supplier_code IS NOT NULL AND NOT is_deleted");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_supplier_code_length",
                table: "fuel_types",
                sql: "supplier_code IS NULL OR length(supplier_code) <= 50");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fuel_types_station_supplier_code",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_supplier_code_length",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "supplier_code",
                table: "fuel_types");
        }
    }
}