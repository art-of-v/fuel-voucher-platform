using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "dismissed_at_utc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When an operator took this money off the worklist. NULL means it is still there. The payment, its journal entry and its statement row are untouched: this hides the row from the queue, it does not delete money.");

            migrationBuilder.AddColumn<Guid>(
                name: "dismissed_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                comment: "Who dismissed it. A copy for the row's own answer; the audit event carries the full act.");

            migrationBuilder.AddColumn<string>(
                name: "dismissed_reason",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Why it left the worklist. Required whenever dismissed_at_utc is set.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_dismissal_pair",
                table: "payments",
                sql: "(dismissed_at_utc IS NULL) = (dismissed_reason IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_dismissal_pair",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "dismissed_at_utc",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "dismissed_by_user_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "dismissed_reason",
                table: "payments");
        }
    }
}
