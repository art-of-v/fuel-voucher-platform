using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Adds the QA staff role. EF generates an empty body here because a role is a seeded ROW,
    /// not a model change - the Designer file and the model snapshot still have to ship so the
    /// migration is discoverable and the snapshot stays in step.
    ///
    /// Level 5 sits strictly between Manager (10) and User (0), and that placement is load
    /// bearing, not cosmetic. SetUserRoleCommandHandler decides "promotion or demotion" by
    /// comparing levels, and a demotion revokes the target's live sessions:
    ///   * level must be &gt; 0, or promoting a User to QA reads as a demotion and logs the
    ///     new tester out on the way in;
    ///   * level must be &lt; 10, or Manager -&gt; QA stops counting as a demotion and the
    ///     manager keeps a session they no longer hold authority for.
    /// </summary>
    public partial class AddQaRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded and idempotent, matching AddRoleLevelAndSeedProductOwnerManager: re-running
            // this (or replaying it onto a database that already has the row) must not fail and
            // must not reset a level an operator has deliberately changed.
            migrationBuilder.Sql("""
                INSERT INTO roles (id, name, created_at_utc, level, is_default)
                VALUES ('7d4a1e93-5c62-4b8f-9a03-e6d1c7b48215', 'QA', NOW() AT TIME ZONE 'UTC', 5, false)
                ON CONFLICT (id) DO UPDATE SET
                    name = EXCLUDED.name,
                    level = EXCLUDED.level,
                    is_default = EXCLUDED.is_default;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Roles guard deletes: staff may still be assigned it, and 'QA' is what
            // RoleHierarchy compares against. ON DELETE RESTRICT on users.role_id turns this
            // into a loud failure rather than a silent orphan, which is the behaviour we want -
            // downgrading past assigned testers is a decision for a human, not for a rollback.
            migrationBuilder.Sql("""
                DELETE FROM roles WHERE id = '7d4a1e93-5c62-4b8f-9a03-e6d1c7b48215';
                """);
        }
    }
}
