using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignKeyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MonobankPaymentUrl",
                table: "orders",
                newName: "monobank_payment_url");

            migrationBuilder.AlterColumn<string>(
                name: "monobank_payment_url",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers",
                column: "assigned_to_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers",
                column: "import_job_id",
                principalTable: "voucher_imports",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments",
                column: "voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_users_assigned_to_user_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_voucher_imports_import_job_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_fulfillments_fuel_vouchers_voucher_id",
                table: "fulfillments");

            migrationBuilder.RenameColumn(
                name: "monobank_payment_url",
                table: "orders",
                newName: "MonobankPaymentUrl");

            migrationBuilder.AlterColumn<string>(
                name: "MonobankPaymentUrl",
                table: "orders",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
