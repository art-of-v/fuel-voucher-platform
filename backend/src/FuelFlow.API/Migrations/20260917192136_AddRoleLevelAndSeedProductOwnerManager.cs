using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleLevelAndSeedProductOwnerManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "roles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                INSERT INTO roles (id, name, created_at_utc, level, is_default)
                VALUES 
                    ('2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c', 'ProductOwner', NOW() AT TIME ZONE 'UTC', 100, false),
                    ('3d5b9c2f-6a4e-4b8f-ad2c-9e3f7a5b6d8e', 'Manager', NOW() AT TIME ZONE 'UTC', 10, false)
                ON CONFLICT (id) DO UPDATE SET
                    name = EXCLUDED.name,
                    level = EXCLUDED.level,
                    is_default = EXCLUDED.is_default;
            ");

            migrationBuilder.Sql(@"
                UPDATE roles SET level = 50, is_default = false WHERE name = 'Admin';
                UPDATE roles SET level = 0, is_default = true WHERE name = 'User';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM roles WHERE id IN (
                    '2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c',
                    '3d5b9c2f-6a4e-4b8f-ad2c-9e3f7a5b6d8e'
                );
            ");

            migrationBuilder.DropColumn(
                name: "is_default",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "level",
                table: "roles");
        }
    }
}
