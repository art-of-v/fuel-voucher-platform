using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAndWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    merchant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    amount_uah = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount_kopecks = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    provider_amount_kopecks = table.Column<decimal>(type: "numeric(14,0)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    webhook_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_payload = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_positive", "amount_uah > 0 AND amount_kopecks > 0");
                    table.CheckConstraint("ck_payments_amount_units_agree", "amount_kopecks = round(amount_uah * 100)");
                    table.CheckConstraint("ck_payments_currency", "currency = 'UAH'");
                    table.CheckConstraint("ck_payments_kind", "kind IN ('Capture','Refund')");
                    table.CheckConstraint("ck_payments_parent_not_self", "parent_payment_id IS DISTINCT FROM id");
                    table.CheckConstraint("ck_payments_provider_amount_non_negative", "provider_amount_kopecks IS NULL OR provider_amount_kopecks > 0");
                    table.CheckConstraint("ck_payments_provider_length", "length(provider) > 0 AND length(provider) <= 20");
                    table.CheckConstraint("ck_payments_refund_shape", "(kind = 'Refund' AND parent_payment_id IS NOT NULL) OR (kind = 'Capture')");
                    table.CheckConstraint("ck_payments_status", "status IN ('Pending','Succeeded','Failed','Cancelled','Expired')");
                    table.CheckConstraint("ck_payments_succeeded_has_paid_at", "status <> 'Succeeded' OR paid_at_utc IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_payments_parent_payment_id",
                        column: x => x.parent_payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_refunds_refund_id",
                        column: x => x.refund_id,
                        principalTable: "refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount_kopecks = table.Column<decimal>(type: "numeric(14,0)", nullable: true),
                    provider_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    provider_modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    signature = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    key_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.id);
                    table.CheckConstraint("ck_webhook_events_amount_non_negative", "amount_kopecks IS NULL OR amount_kopecks > 0");
                    table.CheckConstraint("ck_webhook_events_attempts", "attempts >= 1");
                    table.CheckConstraint("ck_webhook_events_event_id_length", "length(provider_event_id) = 64");
                    table.CheckConstraint("ck_webhook_events_processed", "status NOT IN ('Processed','Ignored') OR processed_at_utc IS NOT NULL");
                    table.CheckConstraint("ck_webhook_events_status", "status IN ('Received','Processed','Ignored','Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_status",
                table: "payments",
                columns: new[] { "order_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_parent_payment_id",
                table: "payments",
                column: "parent_payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_refund_id",
                table: "payments",
                column: "refund_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_webhook_event_id",
                table: "payments",
                column: "webhook_event_id");

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_invoice_kind",
                table: "payments",
                columns: new[] { "provider", "provider_invoice_id", "kind" },
                unique: true,
                filter: "provider_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_invoice_id",
                table: "webhook_events",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_outstanding",
                table: "webhook_events",
                column: "received_at_utc",
                filter: "status IN ('Received','Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_provider_status",
                table: "webhook_events",
                column: "provider_status");

            migrationBuilder.CreateIndex(
                name: "ux_webhook_events_provider_event",
                table: "webhook_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "webhook_events");
        }
    }
}
