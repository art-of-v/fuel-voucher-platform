using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class WorkerMayWorkForNCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members");

            migrationBuilder.CreateIndex(
                name: "ix_company_members_legal_entity_worker",
                table: "company_members",
                columns: new[] { "legal_entity_id", "worker_user_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members",
                column: "worker_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_company_members_legal_entity_worker",
                table: "company_members");

            migrationBuilder.DropIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members");

            migrationBuilder.CreateIndex(
                name: "ix_company_members_worker_user_id",
                table: "company_members",
                column: "worker_user_id",
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
