using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class NotificationTypeAndPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payload",
                table: "notifications",
                type: "jsonb",
                nullable: true,
                comment: "What a client needs to open what the notification is about, as JSON. NULL when there is nothing to open.");

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "notifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "generic",
                comment: "What the notification is about - the routing key the client reads.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_type",
                table: "notifications",
                sql: "type IN ('generic', 'order_fulfilled', 'voucher_issued')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_type",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "payload",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "type",
                table: "notifications");
        }
    }
}
