using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Renames the abandoned-order purge settings to say what they actually purge.
/// </summary>
/// <remarks>
/// <para>
/// The job deletes only soft-deleted, cancelled orders that issued no fuel, past the retention
/// window. The old key, <c>OrderCleanup:*</c>, read as "cleans up orders" — close enough that an
/// operator could reasonably fear it touching purchase history and leave it off forever. Renaming the
/// key without migrating the stored row would silently reset the setting to its default, so the rows
/// move with it.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261007091958_RenameOrderCleanupSettingKeys")]
public partial class RenameOrderCleanupSettingKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE app_settings
            SET key = 'DeletedUnpaidOrderCleanup:Enabled'
            WHERE key = 'OrderCleanup:Enabled';

            UPDATE app_settings
            SET key = 'DeletedUnpaidOrderCleanup:RetentionDays'
            WHERE key = 'OrderCleanup:RetentionDays';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE app_settings
            SET key = 'OrderCleanup:Enabled'
            WHERE key = 'DeletedUnpaidOrderCleanup:Enabled';

            UPDATE app_settings
            SET key = 'OrderCleanup:RetentionDays'
            WHERE key = 'DeletedUnpaidOrderCleanup:RetentionDays';
            """);
    }
}