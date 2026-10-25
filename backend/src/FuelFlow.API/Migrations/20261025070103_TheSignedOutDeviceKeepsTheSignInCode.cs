using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheSignedOutDeviceKeepsTheSignInCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auth_otp_only",
                table: "push_tokens",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "True when the device has signed out: it no longer receives the platform's business notifications, but it stays addressable for the sign-in code. A signed-out device is where a worker signs back in from, so deactivating its token would push the code back onto the paid SMS channel. A successful sign-in clears the flag.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auth_otp_only",
                table: "push_tokens");
        }
    }
}
