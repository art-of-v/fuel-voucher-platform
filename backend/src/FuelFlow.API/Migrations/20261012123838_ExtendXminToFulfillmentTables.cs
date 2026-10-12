using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// #246, second half. Extends the <c>xmin</c> concurrency token to the two tables where traffic and
    /// contention actually are: <c>orders</c> and <c>fuel_vouchers</c>. The detector shipped first
    /// (#1031) instrumenting four quieter tables, because these two were written with raw SQL as well
    /// as through the change tracker, and a token there turned working handlers into 409s.
    /// <para>
    /// That mixing is gone: <c>FulfillmentService</c> now clears the change tracker before the
    /// mismatch-repair pass reads rows its own compare-and-swap statements just claimed, its decision
    /// queries are <c>AsNoTracking</c>, and its repair re-reads the order it saves TRACKED - because EF
    /// materializes a store-generated token only on a tracked load.
    /// </para>
    /// <para>
    /// <b>This migration deliberately executes no SQL,</b> exactly like its predecessor
    /// <c>AddXminConcurrencyToken</c>. <c>xmin</c> is a Postgres system column that already exists on
    /// every row; EF cannot know that and scaffolds <c>ALTER TABLE ... ADD COLUMN "xmin" xid NOT NULL
    /// DEFAULT 0</c>, which Postgres rejects with <c>column name "xmin" conflicts with a system column
    /// name</c>. Auto-deploy runs <c>Migrate()</c> on boot, so an unmodified scaffold here is a crash on
    /// every startup. What the migration does do is record the model change in the snapshot, so
    /// <c>dotnet ef migrations has-pending-model-changes</c> stays clean and the next
    /// <c>migrations add</c> starts from the truth.
    /// </para>
    /// </summary>
    public partial class ExtendXminToFulfillmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. See the type-level summary: xmin is a system column and
            // ADD COLUMN xmin fails. There is nothing to create - the token is a mapping onto
            // something Postgres has always had.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, for the same reason as Up. Rolling back means dropping the
            // shadow property from the model, which the snapshot records; there is no column to
            // drop, and DROP COLUMN xmin would fail the same way ADD COLUMN does.
        }
    }
}