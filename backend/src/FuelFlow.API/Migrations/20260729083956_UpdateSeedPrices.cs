using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSeedPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 51m, 2.0m, 490, 510, 49.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 51m, 2.0m, 980, 1020, 49.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 51m, 2.0m, 2450, 2550, 49.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 500, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2m, 100, 50m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 1000, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2m, 150, 50m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 2500, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 255, 270, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 510, 540, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 1275, 1350, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 56m, 3.0m, 530, 560, 53.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 56m, 3.0m, 1060, 1120, 53.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 56m, 3.0m, 2650, 2800, 53.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.5m, 525, 52.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.5m, 1050, 52.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.5m, 2625, 52.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 61m, 3.0m, 580, 610, 58.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 61m, 3.0m, 1160, 1220, 58.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 61m, 3.0m, 2900, 3050, 58.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 2.0m, 500, 520, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 2.0m, 1000, 1040, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 2.0m, 2500, 2600, 50.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 2.5m, 505, 530, 50.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 2.5m, 1010, 1060, 50.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 2.5m, 2525, 2650, 50.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 510, 51.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 1020, 51.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 2.0m, 2550, 51.0m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 255, 270, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 510, 540, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 27m, 1.5m, 1275, 1350, 25.5m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 54, 51 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29, 27 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 60, 56 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "discount_price",
                value: 61);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55, 52 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56, 53 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29, 27 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 550, 520, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 1100, 1040, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 2750, 2600, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 550, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 110, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 1100, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 165, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 2750, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 300, 280, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 600, 560, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 1500, 1400, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 620, 580, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 1240, 1160, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 58m, 0.10m, 3100, 2900, 57.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 580, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 1160, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 2900, 54.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 650, 600, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 1300, 1200, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 60m, 0.10m, 3250, 3000, 59.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 560, 530, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 1120, 1060, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 53m, 0.10m, 2800, 2650, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 550, 520, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 1100, 1040, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 52m, 0.10m, 2750, 2600, 51.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 560, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 1120, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "margin_uah_per_liter", "original_price", "supplier_price_per_liter" },
                values: new object[] { 0.10m, 2800, 52.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 300, 280, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 600, 560, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "final_price_per_liter", "margin_uah_per_liter", "original_price", "price", "supplier_price_per_liter" },
                values: new object[] { 28m, 0.10m, 1500, 1400, 27.90m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55, 52 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 30, 28 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 62, 58 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                column: "discount_price",
                value: 60);

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56, 53 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55, 52 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 30, 28 });
        }
    }
}
