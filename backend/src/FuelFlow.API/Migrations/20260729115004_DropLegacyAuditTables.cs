using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyAuditTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_package_price_audit");

            migrationBuilder.DropTable(
                name: "price_change_audit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_package_price_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel_name = table.Column<string>(type: "text", nullable: false),
                    new_final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    package_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_package_price_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_change_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    new_base_price = table.Column<int>(type: "integer", nullable: false),
                    new_discount_price = table.Column<int>(type: "integer", nullable: false),
                    old_base_price = table.Column<int>(type: "integer", nullable: false),
                    old_discount_price = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_change_audit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_package_price_audit_changed_at_utc",
                table: "fuel_package_price_audit",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_package_price_audit_package_id",
                table: "fuel_package_price_audit",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_change_audit_changed_at_utc",
                table: "price_change_audit",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_price_change_audit_fuel_type_id",
                table: "price_change_audit",
                column: "fuel_type_id");
        }
    }
}
