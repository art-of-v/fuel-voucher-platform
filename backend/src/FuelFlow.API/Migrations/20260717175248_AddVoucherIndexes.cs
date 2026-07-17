using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_voucher_imports_started_at_utc",
                table: "voucher_imports",
                column: "started_at_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_voucher_imports_status",
                table: "voucher_imports",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_import_job_id",
                table: "fuel_vouchers",
                column: "import_job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_voucher_imports_started_at_utc",
                table: "voucher_imports");

            migrationBuilder.DropIndex(
                name: "IX_voucher_imports_status",
                table: "voucher_imports");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_import_job_id",
                table: "fuel_vouchers");
        }
    }
}
