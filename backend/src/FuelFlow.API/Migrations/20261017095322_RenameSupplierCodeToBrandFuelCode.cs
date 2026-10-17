using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Renames <c>fuel_types.supplier_code</c> to <c>brand_fuel_code</c>. The name was wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <b>brand</b> is a network whose fuel we sell (<c>stations</c>: okko, wog, klo). A
    /// <b>supplier</b> is a third party we buy that fuel from — <c>suppliers</c>,
    /// <c>purchase_batches.supplier_id</c>, and <c>supplier_price_per_liter</c>, which is our cost.
    /// One brand's fuel comes from many suppliers, so a supplier cannot identify a grade: the code
    /// belongs to the brand, is published by the brand's price sheet, and is the brand's to change.
    /// Beside a <c>supplier_price_per_liter</c> column, the old name read as if the reseller named the
    /// grade.
    /// </para>
    /// <para>
    /// Scaffolded, not hand-written: EF emits <c>RenameColumn</c> rather than drop-and-add, so no data
    /// moves and the column's history stays one column. The index and the CHECK are dropped and
    /// recreated because a partial index's definition and a CHECK's expression both carry the column
    /// name, so they cannot survive the rename as they are.
    /// </para>
    /// </remarks>
    public partial class RenameSupplierCodeToBrandFuelCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fuel_types_station_supplier_code",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_supplier_code_length",
                table: "fuel_types");

            migrationBuilder.RenameColumn(
                name: "supplier_code",
                table: "fuel_types",
                newName: "brand_fuel_code");

            migrationBuilder.CreateIndex(
                name: "ux_fuel_types_station_brand_fuel_code",
                table: "fuel_types",
                columns: new[] { "station_id", "brand_fuel_code" },
                unique: true,
                filter: "brand_fuel_code IS NOT NULL AND NOT is_deleted");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_types_brand_fuel_code_length",
                table: "fuel_types",
                sql: "brand_fuel_code IS NULL OR length(brand_fuel_code) <= 50");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_fuel_types_station_brand_fuel_code",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_brand_fuel_code_length",
                table: "fuel_types");

            migrationBuilder.RenameColumn(
                name: "brand_fuel_code",
                table: "fuel_types",
                newName: "supplier_code");

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
    }
}
