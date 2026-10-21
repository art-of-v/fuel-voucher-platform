using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheDatabaseGuardsWhatTheCodeGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The `refunds` view's ext_ref, bounded.
            //
            // It is `COALESCE(o.idempotency_key, o.id::text)`: the order's own key, which is
            // varchar(150), or the order's id as text, which is 36 characters. Coalescing a
            // varchar(150) with a text yields TEXT - and a text column in a view is the one
            // unbounded varchar the schema had left. The cast says what the values already are:
            // neither of them can be longer than 150 characters, so nothing is truncated and the
            // column finally says so.
            //
            // Raw SQL, because the view is not in the EF model: it is a view over `payments` for
            // humans (a spreadsheet, a psql session), and EF has never mapped it. The cast is
            // inside the SELECT list, so the view's own shape - its name, its columns, their
            // order and their names - is unchanged.
            //
            // DROP and CREATE, not CREATE OR REPLACE: PostgreSQL refuses to change a view
            // column's type in place (42P16, "cannot change data type of view column"), and the
            // type is the whole point of the change. Nothing depends on the view - no other view,
            // no function, no grant - so dropping it costs nothing, and the whole thing runs in
            // one transaction: a reader either sees the old view or the new one, never neither.
            migrationBuilder.Sql("DROP VIEW IF EXISTS refunds;");

            migrationBuilder.Sql(
                """
                CREATE VIEW refunds AS
                SELECT
                    p.id                          AS id,
                    p.order_id                    AS order_id,
                    o.user_id                     AS user_id,
                    p.amount_kopecks              AS amount_kopecks,
                    p.provider_invoice_id         AS invoice_id,
                    CAST(COALESCE(o.idempotency_key, o.id::text) AS character varying(150)) AS ext_ref,
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
                LEFT JOIN orders o ON o.id = p.order_id
                WHERE p.kind = 'Refund';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status after. Set for a status change only. One of the order's eight statuses - a CHECK pins the vocabulary.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status after. Set for a status change only.");

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status before. Set for a status change only. One of the order's eight statuses - a CHECK pins the vocabulary.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status before. Set for a status change only.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_events_statuses",
                table: "order_events",
                sql: "(from_status IS NULL OR from_status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled', 'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled')) AND (to_status IS NULL OR to_status IN ('PendingPayment', 'Paid', 'PendingFulfillment', 'PartiallyFulfilled', 'Fulfilled', 'PartiallyRefunded', 'Refunded', 'Cancelled'))");

            migrationBuilder.CreateIndex(
                name: "ux_fulfillments_order_id_voucher_id",
                table: "fulfillments",
                columns: new[] { "order_id", "voucher_id" },
                unique: true,
                filter: "released_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the unbounded form the view had before.
            migrationBuilder.Sql("DROP VIEW IF EXISTS refunds;");

            migrationBuilder.Sql(
                """
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
                LEFT JOIN orders o ON o.id = p.order_id
                WHERE p.kind = 'Refund';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_events_statuses",
                table: "order_events");

            migrationBuilder.DropIndex(
                name: "ux_fulfillments_order_id_voucher_id",
                table: "fulfillments");

            migrationBuilder.AlterColumn<string>(
                name: "to_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status after. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status after. Set for a status change only. One of the order's eight statuses - a CHECK pins the vocabulary.");

            migrationBuilder.AlterColumn<string>(
                name: "from_status",
                table: "order_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The status before. Set for a status change only.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The status before. Set for a status change only. One of the order's eight statuses - a CHECK pins the vocabulary.");
        }
    }
}
