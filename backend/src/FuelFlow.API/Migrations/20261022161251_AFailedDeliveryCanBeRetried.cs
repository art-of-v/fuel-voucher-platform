using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AFailedDeliveryCanBeRetried : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at_utc",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the current attempt started, held as a lease. A redelivery may claim the row once this is NULL or older than the lease; a fresh value means an attempt is in flight.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_events_settled_has_no_claim",
                table: "webhook_events",
                sql: "status NOT IN ('Processed','Ignored') OR processing_started_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_events_settled_has_no_claim",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "processing_started_at_utc",
                table: "webhook_events");
        }
    }
}
