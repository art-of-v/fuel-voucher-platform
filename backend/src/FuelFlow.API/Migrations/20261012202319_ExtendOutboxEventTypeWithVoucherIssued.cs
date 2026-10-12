using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class ExtendOutboxEventTypeWithVoucherIssued : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events",
                sql: "event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',\n               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated',\n               'VoucherIssued')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_event_type",
                table: "outbox_events",
                sql: "event_type IN ('OrderCreated', 'OrderFulfilled', 'OrderCancelled',\n               'PaymentCompleted', 'VoucherExpired', 'VoucherUsed', 'VoucherActivated')");
        }
    }
}
