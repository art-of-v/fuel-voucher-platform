using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheThrowawayRefreshTokenIsRevoked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "refresh_tokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Session",
                comment: "What the token is for. Session = the client keeps and rotates it. DeviceBinding = the throwaway a native sign-in discards once the signed challenge mints the device-bound token; completing that challenge revokes it, and it never outlives the binding window.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purpose",
                table: "refresh_tokens");
        }
    }
}
