using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel_type_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    unit_price = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_line_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_line_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_line_items_order_id",
                table: "order_line_items",
                column: "order_id");

            // Backfill line items for existing orders
            migrationBuilder.Sql("""
                INSERT INTO order_line_items (id, order_id, fuel_type_id, liters, quantity, unit_price, line_total)
                SELECT gen_random_uuid(), o.id, o.fuel_type_id, o.liters, o.quantity, o.price / o.quantity, o.price
                FROM orders o
                WHERE NOT EXISTS (SELECT 1 FROM order_line_items li WHERE li.order_id = o.id)
                AND o.quantity > 0
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_line_items");
        }
    }
}
