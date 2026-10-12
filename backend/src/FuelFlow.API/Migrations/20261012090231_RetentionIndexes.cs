using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_push_tokens_last_seen_at_utc",
                table: "push_tokens",
                column: "last_seen_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_is_read_created_at_utc",
                table: "notifications",
                columns: new[] { "is_read", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_devices_last_seen_at",
                table: "devices",
                column: "last_seen_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_push_tokens_last_seen_at_utc",
                table: "push_tokens");

            migrationBuilder.DropIndex(
                name: "IX_notifications_is_read_created_at_utc",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_devices_last_seen_at",
                table: "devices");
        }
    }
}
