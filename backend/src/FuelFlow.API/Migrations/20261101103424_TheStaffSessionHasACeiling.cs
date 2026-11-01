using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheStaffSessionHasACeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "absolute_expires_at_utc",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The hard stop for a staff panel session, whatever its sliding window says. NULL for every other token and for panel sessions issued before the ceiling existed.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "absolute_expires_at_utc",
                table: "refresh_tokens");
        }
    }
}
