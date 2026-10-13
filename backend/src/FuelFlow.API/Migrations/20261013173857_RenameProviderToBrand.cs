using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameProviderToBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "provider",
                table: "voucher_exchanges",
                newName: "brand");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "purchase_batches",
                newName: "brand");

            migrationBuilder.RenameColumn(
                name: "provider_id",
                table: "provider_event_outbox",
                newName: "scope_id");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "order_line_items",
                newName: "brand");

            migrationBuilder.RenameIndex(
                name: "IX_order_line_items_provider",
                table: "order_line_items",
                newName: "IX_order_line_items_brand");

            migrationBuilder.RenameColumn(
                name: "provider_expiration_date",
                table: "fuel_vouchers",
                newName: "supplier_expiration_date");

            migrationBuilder.RenameColumn(
                name: "provider",
                table: "fuel_vouchers",
                newName: "brand");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_provider_fuel_type_id_liters_status",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_brand_fuel_type_id_liters_status");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_provider_expiration_date",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_supplier_expiration_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "brand",
                table: "voucher_exchanges",
                newName: "provider");

            migrationBuilder.RenameColumn(
                name: "brand",
                table: "purchase_batches",
                newName: "provider");

            migrationBuilder.RenameColumn(
                name: "scope_id",
                table: "provider_event_outbox",
                newName: "provider_id");

            migrationBuilder.RenameColumn(
                name: "brand",
                table: "order_line_items",
                newName: "provider");

            migrationBuilder.RenameIndex(
                name: "IX_order_line_items_brand",
                table: "order_line_items",
                newName: "IX_order_line_items_provider");

            migrationBuilder.RenameColumn(
                name: "supplier_expiration_date",
                table: "fuel_vouchers",
                newName: "provider_expiration_date");

            migrationBuilder.RenameColumn(
                name: "brand",
                table: "fuel_vouchers",
                newName: "provider");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_supplier_expiration_date",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_provider_expiration_date");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_vouchers_brand_fuel_type_id_liters_status",
                table: "fuel_vouchers",
                newName: "IX_fuel_vouchers_provider_fuel_type_id_liters_status");
        }
    }
}
