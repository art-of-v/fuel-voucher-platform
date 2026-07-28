using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFuelPackagePerLiterPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "final_price_per_liter",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "margin_percent",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "margin_uah_per_liter",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "price_updated_at",
                table: "fuel_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "price_updated_by_user_id",
                table: "fuel_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "supplier_price_per_liter",
                table: "fuel_packages",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fuel_package_price_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: false),
                    fuel_name = table.Column<string>(type: "text", nullable: false),
                    old_supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_supplier_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_margin_uah_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_margin_percent = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    old_final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    new_final_price_per_liter = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_package_price_audit", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-95-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-2",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-3",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-dp-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-gas-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-p95-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "okko-pulls-dp-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-100-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-95-euro-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-dp-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-10",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-20",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "fuel_packages",
                keyColumn: "id",
                keyValue: "wog-gas-50",
                columns: new[] { "final_price_per_liter", "margin_percent", "margin_uah_per_liter", "price_updated_at", "price_updated_by_user_id", "supplier_price_per_liter" },
                values: new object[] { null, null, null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_package_price_audit_changed_at_utc",
                table: "fuel_package_price_audit",
                column: "changed_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_package_price_audit_package_id",
                table: "fuel_package_price_audit",
                column: "package_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_package_price_audit");

            migrationBuilder.DropColumn(
                name: "final_price_per_liter",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "margin_percent",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "margin_uah_per_liter",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "price_updated_at",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "price_updated_by_user_id",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "supplier_price_per_liter",
                table: "fuel_packages");
        }
    }
}
