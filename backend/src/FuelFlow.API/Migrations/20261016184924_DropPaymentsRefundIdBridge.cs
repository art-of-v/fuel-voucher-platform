using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropPaymentsRefundIdBridge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payments_refunds_refund_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_refund_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_id",
                table: "payments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "refund_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_refund_id",
                table: "payments",
                column: "refund_id");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_refunds_refund_id",
                table: "payments",
                column: "refund_id",
                principalTable: "refunds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
