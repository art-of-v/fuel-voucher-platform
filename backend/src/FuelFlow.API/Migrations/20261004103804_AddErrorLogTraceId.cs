using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorLogTraceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                table: "error_logs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_error_logs_trace_id",
                table: "error_logs",
                column: "trace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_error_logs_trace_id",
                table: "error_logs");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "error_logs");
        }
    }
}
