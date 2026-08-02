using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "error_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logged_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    exception_type = table.Column<string>(type: "text", nullable: true),
                    exception_message = table.Column<string>(type: "text", nullable: true),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: true),
                    request_path = table.Column<string>(type: "text", nullable: true),
                    request_method = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_error_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_error_logs_level",
                table: "error_logs",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "IX_error_logs_logged_at_utc",
                table: "error_logs",
                column: "logged_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_error_logs_source",
                table: "error_logs",
                column: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "error_logs");
        }
    }
}
