using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ConvertMoneyColumnsToNumeric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                table: "orders",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "original_line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "line_total",
                table: "order_line_items",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "discount_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "base_price",
                table: "fuel_types",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "price",
                table: "fuel_packages",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "original_price",
                table: "fuel_packages",
                type: "numeric(12,2)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 510m, 510m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1020m, 1020m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2550m, 2550m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 520m, 520m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "original_price", "price" },
                values: new object[] { 104m, 104m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1040m, 1040m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "original_price", "price" },
                values: new object[] { 156m, 156m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2600m, 2600m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 270m, 270m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 540m, 540m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1350m, 1350m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 560m, 560m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1120m, 1120m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2800m, 2800m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 550m, 550m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1100m, 1100m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2750m, 2750m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 610m, 610m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1220m, 1220m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 3050m, 3050m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 520m, 520m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1040m, 1040m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2600m, 2600m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 530m, 530m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1060m, 1060m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2650m, 2650m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 530m, 530m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1060m, 1060m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2650m, 2650m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 270m, 270m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 540m, 540m });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1350m, 1350m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 54m, 51m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55m, 52m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29m, 27m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-p95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 60m, 56m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-pulls-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 58m, 55m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 65m, 61m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55m, 52m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-95-euro",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56m, 53m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56m, 53m });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29m, 27m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "price",
                table: "orders",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "unit_price",
                table: "order_line_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "original_line_total",
                table: "order_line_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "line_total",
                table: "order_line_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "discount_price",
                table: "fuel_types",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "base_price",
                table: "fuel_types",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "price",
                table: "fuel_packages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<int>(
                name: "original_price",
                table: "fuel_packages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 510, 510 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1020, 1020 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2550, 2550 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 520, 520 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "original_price", "price" },
                values: new object[] { 104, 104 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1040, 1040 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "original_price", "price" },
                values: new object[] { 156, 156 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2600, 2600 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 270, 270 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 540, 540 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1350, 1350 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 560, 560 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1120, 1120 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2800, 2800 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 550, 550 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1100, 1100 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2750, 2750 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 610, 610 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1220, 1220 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 3050, 3050 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 520, 520 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1040, 1040 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2600, 2600 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 530, 530 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1060, 1060 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2650, 2650 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 530, 530 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1060, 1060 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 2650, 2650 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "original_price", "price" },
                values: new object[] { 270, 270 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "original_price", "price" },
                values: new object[] { 540, 540 });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "original_price", "price" },
                values: new object[] { 1350, 1350 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-95",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 54, 51 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "okko-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 55, 52 });

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
                keyValue: "okko-pulls-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 58, 55 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-100",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 65, 61 });

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
                keyValue: "wog-dp",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 56, 53 });

            migrationBuilder.UpdateData(
                table: "fuel_types",
                keyColumn: "id",
                keyValue: "wog-gas",
                columns: new[] { "base_price", "discount_price" },
                values: new object[] { 29, 27 });
        }
    }
}
