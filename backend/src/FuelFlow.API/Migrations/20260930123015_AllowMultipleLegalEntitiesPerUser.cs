using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleLegalEntitiesPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities");

            migrationBuilder.CreateIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities");

            migrationBuilder.CreateIndex(
                name: "IX_legal_entities_user_id",
                table: "legal_entities",
                column: "user_id",
                unique: true);
        }
    }
}
