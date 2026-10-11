using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ConstrainAuditReplacementVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_fulfilled_voucher_id",
                table: "voucher_renewal_items",
                column: "fulfilled_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_operator_voucher_renewals_replacement_voucher_id",
                table: "operator_voucher_renewals",
                column: "replacement_voucher_id");

            migrationBuilder.AddForeignKey(
                name: "FK_operator_voucher_renewals_fuel_vouchers_replacement_voucher~",
                table: "operator_voucher_renewals",
                column: "replacement_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_exchanges_fuel_vouchers_new_voucher_id",
                table: "voucher_exchanges",
                column: "new_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_renewal_items_fuel_vouchers_fulfilled_voucher_id",
                table: "voucher_renewal_items",
                column: "fulfilled_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_operator_voucher_renewals_fuel_vouchers_replacement_voucher~",
                table: "operator_voucher_renewals");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_exchanges_fuel_vouchers_new_voucher_id",
                table: "voucher_exchanges");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_renewal_items_fuel_vouchers_fulfilled_voucher_id",
                table: "voucher_renewal_items");

            migrationBuilder.DropIndex(
                name: "IX_voucher_renewal_items_fulfilled_voucher_id",
                table: "voucher_renewal_items");

            migrationBuilder.DropIndex(
                name: "IX_operator_voucher_renewals_replacement_voucher_id",
                table: "operator_voucher_renewals");
        }
    }
}
