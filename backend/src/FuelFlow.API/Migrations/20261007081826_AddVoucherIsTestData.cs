using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherIsTestData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_test_data",
                table: "fuel_vouchers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status_is_test_data",
                table: "fuel_vouchers",
                columns: new[] { "status", "is_test_data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status_is_test_data",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "is_test_data",
                table: "fuel_vouchers");
        }
    }
}
