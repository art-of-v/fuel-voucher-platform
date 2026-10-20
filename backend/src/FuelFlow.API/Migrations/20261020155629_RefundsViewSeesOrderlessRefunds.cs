using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The <c>refunds</c> view sees a refund that has no order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view joined <c>payments</c> to <c>orders</c> with an INNER join. The shape constraint
    /// (<c>ck_payments_refund_shape</c>) requires a refund to have a
    /// <c>parent_payment_id</c> - it does NOT require an <c>order_id</c>, and
    /// <c>ix_payments_unallocated</c> exists precisely for the payments that arrive without one. A
    /// refund like that was therefore invisible in the view, with no error anywhere: the row was
    /// in the table, and the human's view of refunds silently omitted it.
    /// </para>
    /// <para>
    /// There are no such rows today, which is exactly why this is cheap to fix now and expensive
    /// to fix after the first one - the view exists for humans (a spreadsheet, a psql session), and
    /// a human's view of refunds that silently omits one is a defect waiting for its first
    /// occurrence.
    /// </para>
    /// <para>
    /// A LEFT join closes it: the refund is in the view, and the columns that come from the order
    /// are NULL for a refund that has none. <c>CREATE OR REPLACE VIEW</c> is enough - the columns
    /// and their types do not change, only which rows the join keeps.
    /// </para>
    /// </remarks>
    public partial class RefundsViewSeesOrderlessRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW refunds AS
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE VIEW refunds AS
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
                WHERE p.kind = 'Refund';
                """);
        }
    }
}
