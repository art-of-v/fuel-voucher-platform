using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SeedUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The built-in role for mobile-app customers, complementing the "Admin"
            // role seeded in 20260619000001_SeedAdminRole. Fixed id mirrors that seed.
            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "name", "created_at_utc" },
                values: new object[] { new Guid("a0000000-0000-0000-0000-000000000002"), "User", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });

            // Backfill every existing role-less account as a mobile customer. Admins already
            // have a role, so they are untouched; new registrations are stamped by the verify handler.
            migrationBuilder.Sql(
                "UPDATE users SET role_id = 'a0000000-0000-0000-0000-000000000002' WHERE role_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE users SET role_id = NULL WHERE role_id = 'a0000000-0000-0000-0000-000000000002';");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("a0000000-0000-0000-0000-000000000002"));
        }
    }
}
