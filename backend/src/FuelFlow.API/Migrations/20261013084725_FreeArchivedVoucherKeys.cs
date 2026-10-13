using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class FreeArchivedVoucherKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                column: "qr_payload",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers",
                column: "voucher_number",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_qr_payload",
                table: "fuel_vouchers",
                column: "qr_payload",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_voucher_number",
                table: "fuel_vouchers",
                column: "voucher_number",
                unique: true);
        }
    }
}
