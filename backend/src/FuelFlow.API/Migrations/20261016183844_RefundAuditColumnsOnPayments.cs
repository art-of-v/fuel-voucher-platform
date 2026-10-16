using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RefundAuditColumnsOnPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_name",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_automatic",
                table: "payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_payments_created_by_user_id",
                table: "payments",
                column: "created_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments",
                sql: "kind = 'Refund' OR (created_by_user_id IS NULL AND created_by_user_name IS NULL AND NOT is_automatic AND error_message IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_users_created_by_user_id",
                table: "payments",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payments_users_created_by_user_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_created_by_user_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refund_audit_only_on_refund",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "created_by_user_name",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "is_automatic",
                table: "payments");
        }
    }
}
