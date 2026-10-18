using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SettlementMessagePaymentForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_settlement_messages_payment_id",
                table: "settlement_messages",
                column: "payment_id");

            migrationBuilder.AddForeignKey(
                name: "FK_settlement_messages_payments_payment_id",
                table: "settlement_messages",
                column: "payment_id",
                principalTable: "payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_settlement_messages_payments_payment_id",
                table: "settlement_messages");

            migrationBuilder.DropIndex(
                name: "IX_settlement_messages_payment_id",
                table: "settlement_messages");
        }
    }
}
