using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRedundantOrderColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "order_line_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                @"UPDATE ""order_line_items"" SET ""provider"" = ""orders"".""provider"" FROM ""orders"" WHERE ""order_line_items"".""order_id"" = ""orders"".""id""");

            migrationBuilder.DropColumn(
                name: "fuel_type_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "liters",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "product_type",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider",
                table: "order_line_items");

            migrationBuilder.AddColumn<string>(
                name: "fuel_type_id",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "liters",
                table: "orders",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "product_type",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }
    }
}
