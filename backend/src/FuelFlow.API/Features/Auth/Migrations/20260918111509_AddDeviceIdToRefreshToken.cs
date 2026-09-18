using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Features.Auth.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceIdToRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_id",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id_device_id",
                table: "refresh_tokens",
                columns: new[] { "user_id", "device_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_user_id_device_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "refresh_tokens");
        }
    }
}
