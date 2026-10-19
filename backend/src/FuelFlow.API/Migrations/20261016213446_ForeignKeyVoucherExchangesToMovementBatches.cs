using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13 / planning issue #319, stage 2 of 4: give <c>voucher_exchanges.exchange_batch_id</c> the
    /// parent it has been naming since 2026-10-01.
    ///
    /// <para>
    /// Two steps in one migration, in this order, and the order is the whole point:
    /// </para>
    ///
    /// <para>
    /// <b>1. Backfill, so no row is left parentless.</b> One batch row per distinct
    /// <c>exchange_batch_id</c>, with the facts the readers used to re-derive: the time
    /// (<c>MIN(created_at_utc)</c>), the supplier and the operator name (<c>DISTINCT ON</c> the
    /// earliest row, matching what <c>GetVoucherExchangeBatchesQueryHandler</c> did with
    /// <c>g.First()</c>), and the surcharge as the <b>sum of the member rows' shares</b> — not the
    /// command total, which was never stored anywhere. That is the only number the existing rows can
    /// support, and it is what the screens show today.
    /// </para>
    ///
    /// <para>
    /// Every existing batch was created by a confirm, so every one is <c>confirmed</c> with
    /// <c>confirmed_at_utc</c> set to its opening time. Nothing can be <c>draft</c> or
    /// <c>rejected</c>: the application has no path that produces either.
    /// </para>
    ///
    /// <para>
    /// <b>2. Then the foreign key.</b> Adding the constraint before the backfill would fail outright
    /// on any database that holds a single exchange — which is every staging database and every
    /// integration fixture, even though production holds none.
    /// </para>
    ///
    /// <para>
    /// <b>Reversible.</b> <c>Down</c> drops the constraint and the table. The member rows are
    /// untouched either way: <c>exchange_batch_id</c> was already <c>NOT NULL</c> before this
    /// migration and still is, so rolling back restores exactly the schema that existed, with the
    /// same data in it.
    /// </para>
    /// </summary>
    public partial class ForeignKeyVoucherExchangesToMovementBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BEFORE the constraint. A row without a parent cannot satisfy a RESTRICT foreign key.
            migrationBuilder.Sql("""
                INSERT INTO movement_batches
                    (id, kind, supplier_id, status, surcharge_uah,
                     opened_by_user_id, opened_by_user_name, opened_at_utc,
                     confirmed_at_utc, rejection_reason, created_at_utc)
                SELECT e.exchange_batch_id,
                       'voucher_exchange',
                       -- Earliest row's value, which is what the readers used to take with g.First().
                       (array_agg(e.supplier_id ORDER BY e.created_at_utc))[1],
                       'confirmed',
                       -- The sum of the members' own shares: what the batch screens reported before
                       -- this table existed. The command total was never stored anywhere, so the sum
                       -- is the only figure these rows can honestly carry. New batches record the
                       -- command total directly - see MovementBatch.SurchargeUah for why the two
                       -- agree except when a voucher is returned without a replacement.
                       SUM(e.surcharge_uah),
                       (array_agg(e.acting_user_id ORDER BY e.created_at_utc))[1],
                       -- First NON-NULL name in time order, matching the FirstOrDefault(n => n is not
                       -- null) the list handler used.
                       (array_agg(e.acting_user_name ORDER BY (e.acting_user_name IS NULL), e.created_at_utc))[1],
                       MIN(e.created_at_utc),
                       MIN(e.created_at_utc),
                       NULL,
                       MIN(e.created_at_utc)
                FROM voucher_exchanges e
                GROUP BY e.exchange_batch_id;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_exchanges_movement_batches_exchange_batch_id",
                table: "voucher_exchanges",
                column: "exchange_batch_id",
                principalTable: "movement_batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_voucher_exchanges_movement_batches_exchange_batch_id",
                table: "voucher_exchanges");

            // Only the constraint goes. The TABLE belongs to MovementBatchTable's Down, and a
            // migration that drops another migration's object makes the rollback order impossible:
            // EF reverses newest-first, so this Down would run first, drop the table, and then
            // MovementBatchTable.Down() would fail on a table that is no longer there.
            //
            // The batch rows go with it, and that is the right outcome - they are derived from
            // voucher_exchanges by the backfill above, so a forward migration rebuilds them, and the
            // schema this rolls back to never had them. The member rows are untouched either way:
            // exchange_batch_id was NOT NULL before this migration and still is.
        }
    }
}