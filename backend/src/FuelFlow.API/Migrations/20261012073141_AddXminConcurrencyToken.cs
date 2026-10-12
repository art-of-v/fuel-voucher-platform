using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// #246 detector. Adds the <c>xmin</c> concurrency token to four tables.
    /// <para>
    /// <b>This migration deliberately executes no SQL.</b> <c>xmin</c> is a Postgres system column that
    /// already exists on every row; EF cannot know that and scaffolds
    /// <c>ALTER TABLE ... ADD COLUMN "xmin" xid NOT NULL DEFAULT 0</c>, which Postgres rejects with
    /// <c>column name "xmin" conflicts with a system column name</c>. Because auto-deploy runs
    /// <c>Migrate()</c> on boot, an unmodified scaffold here is a crash on every startup.
    /// </para>
    /// <para>
    /// What the migration does do is record the model change in the snapshot, so
    /// <c>dotnet ef migrations has-pending-model-changes</c> stays clean and the next
    /// <c>migrations add</c> starts from the truth.
    /// </para>
    /// </summary>
    public partial class AddXminConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. See the type-level summary: `xmin` is a system column and
            // `ADD COLUMN xmin` fails. There is nothing to create - the token is a mapping onto
            // something Postgres has always had.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, for the same reason as Up. Rolling back means dropping the
            // shadow property from the model, which the snapshot records; there is no column to
            // drop, and `DROP COLUMN xmin` would fail the same way `ADD COLUMN` does.
        }
    }
}