using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Features.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleNameAtIssueToRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role_name_at_issue",
                table: "refresh_tokens",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role_name_at_issue",
                table: "refresh_tokens");
        }
    }
}
