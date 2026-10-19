using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RefundsBecomeAViewOverPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refunds");

            // Replaced immediately by a view of the same name and the same column names, so an
            // operator with a saved query, a spreadsheet, or a psql session against `refunds` keeps
            // working. Nothing in the application reads it any more - this is for the humans and for
            // anything that has not been rewritten yet.
            //
            // The two joins are what the table used to copy rather than reference: user_id and
            // ext_ref are `orders.user_id` and `orders.idempotency_key`, denormalised onto `refunds`
            // when a refund was written. Reading the order is the truth and cannot drift.
            migrationBuilder.Sql(@"
CREATE VIEW refunds AS
SELECT
    p.id                          AS id,
    p.order_id                    AS order_id,
    o.user_id                     AS user_id,
    p.amount_kopecks              AS amount_kopecks,
    p.provider_invoice_id         AS invoice_id,
    COALESCE(o.idempotency_key, o.id::text) AS ext_ref,
    CASE p.status
        WHEN 'Pending'   THEN 'Processing'
        WHEN 'Succeeded' THEN 'Completed'
        ELSE 'Failed'
    END                           AS status,
    CASE p.status
        WHEN 'Pending'   THEN 'processing'
        WHEN 'Succeeded' THEN 'success'
        WHEN 'Failed'    THEN 'failure'
        ELSE NULL
    END                           AS monobank_status,
    p.error_message               AS error_message,
    p.created_by_user_id          AS created_by_user_id,
    p.created_by_user_name        AS created_by_user_name,
    p.is_automatic                AS is_automatic,
    p.created_at_utc              AS created_at_utc,
    p.updated_at_utc              AS updated_at_utc
FROM payments p
JOIN orders o ON o.id = p.order_id
WHERE p.kind = 'Refund';");

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_refund_per_order",
                table: "payments",
                column: "order_id",
                unique: true,
                filter: "kind = 'Refund'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropped before the table is recreated below, or CreateTable would collide with it.
            migrationBuilder.Sql("DROP VIEW IF EXISTS refunds;");

            migrationBuilder.DropIndex(
                name: "ux_payments_one_refund_per_order",
                table: "payments");

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_kopecks = table.Column<decimal>(type: "numeric(14,0)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ext_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_automatic = table.Column<bool>(type: "boolean", nullable: false),
                    monobank_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.id);
                    table.CheckConstraint("ck_refund_amount_positive", "amount_kopecks > 0");
                    table.CheckConstraint("ck_refund_status", "status IN ('Processing', 'Completed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_refunds_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refunds_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_created_by_user_id",
                table: "refunds",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_invoice_id",
                table: "refunds",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_order_id",
                table: "refunds",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refunds_user_id",
                table: "refunds",
                column: "user_id");
        }
    }
}
