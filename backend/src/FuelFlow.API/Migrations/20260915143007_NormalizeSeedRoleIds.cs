using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeSeedRoleIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the hand-made "a0000000-...-0001/2" seed ids from 20260619000001_SeedAdminRole
            // and 20260915121550_SeedUserRole with real (v4) UUIDs. users.role_id is an
            // ON UPDATE NO ACTION FK, so re-key the child rows before the role ids themselves.
            migrationBuilder.Sql(@"
                UPDATE users SET role_id = '0b6c503a-2086-4fe3-b617-b47385b474bd'
                    WHERE role_id = 'a0000000-0000-0000-0000-000000000001';
                UPDATE users SET role_id = '1b445bd0-6f91-4b5a-ac2d-a9601691142f'
                    WHERE role_id = 'a0000000-0000-0000-0000-000000000002';
                UPDATE roles SET id = '0b6c503a-2086-4fe3-b617-b47385b474bd'
                    WHERE id = 'a0000000-0000-0000-0000-000000000001';
                UPDATE roles SET id = '1b445bd0-6f91-4b5a-ac2d-a9601691142f'
                    WHERE id = 'a0000000-0000-0000-0000-000000000002';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE users SET role_id = 'a0000000-0000-0000-0000-000000000001'
                    WHERE role_id = '0b6c503a-2086-4fe3-b617-b47385b474bd';
                UPDATE users SET role_id = 'a0000000-0000-0000-0000-000000000002'
                    WHERE role_id = '1b445bd0-6f91-4b5a-ac2d-a9601691142f';
                UPDATE roles SET id = 'a0000000-0000-0000-0000-000000000001'
                    WHERE id = '0b6c503a-2086-4fe3-b617-b47385b474bd';
                UPDATE roles SET id = 'a0000000-0000-0000-0000-000000000002'
                    WHERE id = '1b445bd0-6f91-4b5a-ac2d-a9601691142f';");
        }
    }
}
