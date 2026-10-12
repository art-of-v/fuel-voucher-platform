using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelPackageTestFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_test_data",
                table: "fuel_packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                column: "is_test_data",
                value: false);

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                column: "is_test_data",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_is_deleted_is_test_data",
                table: "fuel_packages",
                columns: new[] { "is_deleted", "is_test_data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_packages_is_deleted_is_test_data",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "is_test_data",
                table: "fuel_packages");
        }
    }
}
