using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropFuelPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_packages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_packages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    fuel_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    min_discount_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    original_price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    price_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    price_updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pump_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    station_id = table.Column<string>(type: "text", nullable: false),
                    supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_packages", x => x.id);
                    table.CheckConstraint("ck_fuel_packages_data_kind", "data_kind IN ('real', 'qa')");
                    table.CheckConstraint("ck_fuel_packages_fuel_type_id_length", "length(fuel_type_id) <= 100");
                    table.CheckConstraint("ck_fuel_packages_id_length", "length(id) <= 100");
                    table.CheckConstraint("ck_fuel_packages_liters_positive", "liters > 0");
                    table.CheckConstraint("ck_fuel_packages_price_non_negative", "price >= 0");
                    table.CheckConstraint("ck_fuel_packages_station_id_length", "length(station_id) <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_fuel_type_id",
                table: "fuel_packages",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_is_deleted_data_kind",
                table: "fuel_packages",
                columns: new[] { "is_deleted", "data_kind" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_station_id",
                table: "fuel_packages",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_station_id_fuel_type_id",
                table: "fuel_packages",
                columns: new[] { "station_id", "fuel_type_id" });
        }
    }
}
