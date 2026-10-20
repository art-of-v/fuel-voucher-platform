using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class OneVoucherOneLiveFulfilment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // **One voucher, one live fulfilment**, enforced by the database.
            //
            // The claim is atomic in the application - a compare-and-swap on the fulfilment
            // row - and the application is the thing that can be wrong. Two live fulfilments for
            // one voucher hand the same bearer instrument to two customers, and a voucher is
            // money. The invariant belongs where it cannot be forgotten.
            //
            // The index is PARTIAL on the same predicate as the entity's global query filter,
            // so it covers exactly the rows that are live: every row EF can see, and every row
            // the raw claim statement touches. Nothing reads the released rows by voucher id -
            // the wallet's history is assembled in memory from the order's line items - so the
            // plain index this replaces was serving the same rows and nothing else.
            //
            // CONCURRENTLY, and outside a transaction. A plain CREATE INDEX takes an ACCESS
            // EXCLUSIVE lock on the table for the length of the build, and this table grows by
            // one row for every voucher the platform issues. CONCURRENTLY does not take that
            // lock, and it cannot run inside a transaction - which is why this migration is one
            // of the two in the history that does not run in one.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_fulfillments_one_live_per_voucher
                ON fulfillments (voucher_id)
                WHERE released_at_utc IS NULL;
                """,
                suppressTransaction: true);

            // The plain index goes AFTER the unique one is in place, never before: a window
            // with no index on voucher_id at all would make every live lookup a sequential
            // scan, and this is the table that grows with every voucher issued.
            //
            // It is raw SQL for the same reason the CREATE is: EF refuses to mix a
            // transaction-suppressed operation with an operation it would otherwise wrap in a
            // transaction, and this migration must not run in one.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_fulfillments_voucher_id;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The plain index comes back FIRST, so the table is never left without one.
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_fulfillments_voucher_id
                ON fulfillments (voucher_id);
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ux_fulfillments_one_live_per_voucher;",
                suppressTransaction: true);
        }
    }
}
