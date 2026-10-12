using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RestrictMoneyRecordsToOrderDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_refunds_orders_order_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_renewal_items_orders_order_id",
                table: "voucher_renewal_items");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_orders_order_id",
                table: "refunds",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_renewal_items_orders_order_id",
                table: "voucher_renewal_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_refunds_orders_order_id",
                table: "refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_renewal_items_orders_order_id",
                table: "voucher_renewal_items");

            migrationBuilder.AddForeignKey(
                name: "FK_refunds_orders_order_id",
                table: "refunds",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_renewal_items_orders_order_id",
                table: "voucher_renewal_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
