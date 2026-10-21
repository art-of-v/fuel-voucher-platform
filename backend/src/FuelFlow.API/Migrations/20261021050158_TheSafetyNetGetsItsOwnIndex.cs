using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheSafetyNetGetsItsOwnIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_events_created_at_utc",
                table: "outbox_events");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pending_consumed",
                table: "outbox_events",
                column: "created_at_utc",
                filter: "processed = false AND event_type IN ('OrderCreated', 'OrderFulfilled', 'VoucherIssued')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_events_pending_consumed",
                table: "outbox_events");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_created_at_utc",
                table: "outbox_events",
                column: "created_at_utc");
        }
    }
}
