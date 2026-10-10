using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPumpAndMinDiscountToFuelPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "min_discount_per_liter",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pump_price_per_liter",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "min_discount_per_liter", "pump_price_per_liter" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "min_discount_per_liter",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "pump_price_per_liter",
                table: "fuel_packages");
        }
    }
}
