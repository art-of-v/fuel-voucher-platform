using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SeedFuelPackagePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 55m, 0.10m, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 55m, 0.10m, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 55m, 0.10m, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 27.90m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "supplier_price_per_liter" },
                values: new object[] { null, null, null });
        }
    }
}
