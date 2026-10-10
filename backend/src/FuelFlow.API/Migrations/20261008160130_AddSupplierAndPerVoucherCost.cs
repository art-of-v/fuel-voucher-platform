using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAndPerVoucherCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_batches_fuel_type_id",
                table: "purchase_batches");

            migrationBuilder.DropColumn(
                name: "cost_per_liter",
                table: "purchase_batches");

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "cost_per_liter",
                table: "fuel_vouchers",
                type: "numeric(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_info = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    station_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_supplier_id",
                table: "voucher_exchanges",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_batches_supplier_id",
                table: "purchase_batches",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status",
                table: "fuel_vouchers",
                columns: new[] { "fuel_type_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_supplier_id",
                table: "fuel_vouchers",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_is_active",
                table: "suppliers",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_name",
                table: "suppliers",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_suppliers_supplier_id",
                table: "fuel_vouchers",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_suppliers_supplier_id",
                table: "purchase_batches",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_exchanges_suppliers_supplier_id",
                table: "voucher_exchanges",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_suppliers_supplier_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_suppliers_supplier_id",
                table: "purchase_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_exchanges_suppliers_supplier_id",
                table: "voucher_exchanges");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_voucher_exchanges_supplier_id",
                table: "voucher_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_purchase_batches_supplier_id",
                table: "purchase_batches");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_fuel_type_id_status",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_supplier_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "voucher_exchanges");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "purchase_batches");

            migrationBuilder.DropColumn(
                name: "cost_per_liter",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "fuel_vouchers");

            migrationBuilder.AddColumn<decimal>(
                name: "cost_per_liter",
                table: "purchase_batches",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_batches_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id");
        }
    }
}
