using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorLogResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at_utc",
                table: "error_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by_user_id",
                table: "error_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolved_by_user_name",
                table: "error_logs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_error_logs_resolved_at_utc_logged_at_utc",
                table: "error_logs",
                columns: new[] { "resolved_at_utc", "logged_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_error_logs_resolved_at_utc_logged_at_utc",
                table: "error_logs");

            migrationBuilder.DropColumn(
                name: "resolved_at_utc",
                table: "error_logs");

            migrationBuilder.DropColumn(
                name: "resolved_by_user_id",
                table: "error_logs");

            migrationBuilder.DropColumn(
                name: "resolved_by_user_name",
                table: "error_logs");
        }
    }
}
