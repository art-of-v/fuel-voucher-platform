using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherVerificationMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verification_mismatched_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verification_total_modules",
                table: "fuel_vouchers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verification_mismatch_percent",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "verification_mismatched_modules",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "verification_total_modules",
                table: "fuel_vouchers");
        }
    }
}
