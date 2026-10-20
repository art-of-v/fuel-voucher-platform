using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class SupplierEdrIpnIsUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_suppliers_edr_ipn",
                table: "suppliers",
                column: "edr_ipn",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_suppliers_edr_ipn_format",
                table: "suppliers",
                sql: "\"edr_ipn\" IS NULL OR \"edr_ipn\" ~ '^[0-9]{8,10}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_suppliers_edr_ipn",
                table: "suppliers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_suppliers_edr_ipn_format",
                table: "suppliers");
        }
    }
}
