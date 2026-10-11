using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteCompanyMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_company_members_worker_user_id",
                table: "company_members");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                table: "company_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                table: "company_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members",
                column: "worker_user_id",
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members");

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                table: "company_members");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                table: "company_members");

            migrationBuilder.CreateIndex(
                name: "IX_company_members_worker_user_id",
                table: "company_members",
                column: "worker_user_id",
                unique: true);
        }
    }
}
