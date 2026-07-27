using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceChangeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "price_change_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    old_base_price = table.Column<int>(type: "integer", nullable: false),
                    new_base_price = table.Column<int>(type: "integer", nullable: false),
                    old_discount_price = table.Column<int>(type: "integer", nullable: false),
                    new_discount_price = table.Column<int>(type: "integer", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_change_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_change_audit_changed_at_utc",
                table: "price_change_audit",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_price_change_audit_fuel_type_id",
                table: "price_change_audit",
                column: "fuel_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_change_audit");
        }
    }
}
