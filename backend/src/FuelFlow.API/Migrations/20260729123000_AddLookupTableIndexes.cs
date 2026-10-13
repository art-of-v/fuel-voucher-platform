using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupTableIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_stations_name",
                table: "stations",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_stations_station_type",
                table: "stations",
                column: "station_type");

            migrationBuilder.CreateIndex(
                name: "IX_order_line_items_fuel_type_id",
                table: "order_line_items",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_line_items_provider",
                table: "order_line_items",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_name",
                table: "fuel_types",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types",
                columns: new[] { "station_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_station_id_fuel_type_id",
                table: "fuel_packages",
                columns: new[] { "station_id", "fuel_type_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stations_name",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "IX_stations_station_type",
                table: "stations");

            migrationBuilder.DropIndex(
                name: "IX_order_line_items_fuel_type_id",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "IX_order_line_items_provider",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "IX_fuel_types_name",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "IX_fuel_types_station_id_name",
                table: "fuel_types");

            migrationBuilder.DropIndex(
                name: "IX_fuel_packages_station_id_fuel_type_id",
                table: "fuel_packages");
        }
    }
}
