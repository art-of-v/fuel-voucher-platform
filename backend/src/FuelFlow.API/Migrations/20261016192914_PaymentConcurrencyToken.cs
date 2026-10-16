using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// #246 detector, third table group. Moves the <c>xmin</c> concurrency token onto <c>payments</c>.
    /// <para>
    /// The token arrived with the refunds this table absorbed. <c>refunds</c> had one and
    /// <c>payments</c> did not, so for the whole life of the two tables a refund's protection lived
    /// on the twin row rather than on the row everything actually read and wrote. A retry racing the
    /// status sync could both save and the loser's write vanished with nothing logged. With one
    /// table there is nothing left to protect on but this row.
    /// </para>
    /// <para>
    /// <b>This migration deliberately executes no SQL</b>, exactly like
    /// <c>AddXminConcurrencyToken</c> and <c>ExtendXminToFulfillmentTables</c>. <c>xmin</c> is a
    /// Postgres system column that already exists on every row; there is nothing to create.
    /// </para>
    /// <para>
    /// <b>It was written as an explicit <c>AddColumn</c> first, and that was a trap.</b> It emitted
    /// no DDL - checked against <c>dotnet ef migrations script --idempotent</c>, with and without
    /// <c>rowVersion: true</c> - so it would have worked. But it looks identical to the scaffold
    /// both predecessors document as a crash on every startup, so the next reader has to either
    /// trust a provider behaviour nobody wrote down or go looking for the bug. An empty body with the
    /// reason in it cannot be misread and cannot regress by someone editing a parameter.
    /// </para>
    /// </summary>
    public partial class PaymentConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. See the type-level summary: `xmin` is a system column, already
            // present on every row of every table, so there is nothing to add. What this migration
            // does do is record the model change in the snapshot, which is what keeps
            // `has-pending-model-changes` clean and the next `migrations add` starting from the truth.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, for the same reason as Up: rolling back means dropping the shadow
            // property from the model, which the snapshot records. There is no column to drop, and
            // `DROP COLUMN xmin` would fail exactly as `ADD COLUMN xmin` does.
        }
    }
}
