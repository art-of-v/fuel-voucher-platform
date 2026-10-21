using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class OneBasketOnePendingOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "This order's own key: unique per order, and the external reference the acquirer is given when the order is refunded. The checkout's identity is checkout_key - two requests for one basket are one order through that column, not through this one.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "The key the customer sent with the request. Two requests with the same key are one order,which is what makes a double-tap one order and not two.");

            migrationBuilder.AddColumn<string>(
                name: "checkout_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "The basket this order is the live attempt for: the customer, the brand, the fuel, the litres and the quantity (a digest of the whole cart for a bulk checkout). Two orders may not hold the same basket while both await payment, so a double tap and a retried request are one order and one invoice.");

            migrationBuilder.CreateIndex(
                name: "ix_orders_checkout_key",
                table: "orders",
                column: "checkout_key",
                unique: true,
                filter: "checkout_key IS NOT NULL AND status = 'PendingPayment' AND is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_checkout_key",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "checkout_key",
                table: "orders");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "The key the customer sent with the request. Two requests with the same key are one order,which is what makes a double-tap one order and not two.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "This order's own key: unique per order, and the external reference the acquirer is given when the order is refunded. The checkout's identity is checkout_key - two requests for one basket are one order through that column, not through this one.");
        }
    }
}
