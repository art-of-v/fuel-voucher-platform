using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStationSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "stations",
                type: "integer",
                nullable: false,
                defaultValue: 999);

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "klo",
                column: "sort_order",
                value: 4);

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "okko",
                column: "sort_order",
                value: 1);

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "upg",
                column: "sort_order",
                value: 3);

            migrationBuilder.UpdateData(
                table: "stations",
                keyColumn: "id",
                keyValue: "wog",
                column: "sort_order",
                value: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "stations");
        }
    }
}
