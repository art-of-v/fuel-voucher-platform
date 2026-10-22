using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheRetentionPurgeReadsThroughAnIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -------------------------------------------------------------------------------
            // The nightly retention purge reads the outbox through an index of its own.
            //
            // The purge removes two sets of rows, aged by two different columns: the events that
            // were published, aged by when they were published, and the events that nobody will
            // ever read again - the types with no consumer - aged by when they happened.
            //
            // Neither half had an index to read through. The only index that led created_at_utc
            // was the PARTIAL one the dispatchers' safety net reads (ix_outbox_events_pending_
            // consumed), and its filter is `processed = false AND event_type IN (the consumed
            // types)` - the opposite set from the purge's second half, which asks for the events
            // whose type is NOT consumed. A partial index cannot serve a predicate that
            // contradicts its filter, so the purge read the whole table: about two hundred
            // thousand rows, every night, to delete a few thousand.
            //
            // The first index is COMPOSITE rather than a plain (created_at_utc) because EF Core
            // cannot hold two indexes over the same columns: a second HasIndex over the same
            // property set returns the first one and edits it. That is how the plain
            // created_at_utc index became the partial one in the first place (migration
            // 20261021050158), and it is why the plain index has to be spelled as a different
            // property set. The id also makes the order total, which is what a batched delete
            // wants.
            //
            // The second index leads processed_at_utc, which is the column the first half of the
            // purge ages by. No index led it, so that half had nothing to read through either.
            //
            // Both are created the plain way, without CONCURRENTLY: the table is small enough
            // that the ACCESS EXCLUSIVE lock is a moment, and the migration runs inside the
            // deploy's own transaction like every other one here.
            // -------------------------------------------------------------------------------
            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_created_at_utc_id",
                table: "outbox_events",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_processed_at_utc",
                table: "outbox_events",
                column: "processed_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_events_created_at_utc_id",
                table: "outbox_events");

            migrationBuilder.DropIndex(
                name: "ix_outbox_events_processed_at_utc",
                table: "outbox_events");
        }
    }
}
