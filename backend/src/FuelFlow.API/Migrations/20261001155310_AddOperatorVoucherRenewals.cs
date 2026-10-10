using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorVoucherRenewals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operator_voucher_renewals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    term_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    old_expiration = table.Column<DateOnly>(type: "date", nullable: false),
                    new_expiration = table.Column<DateOnly>(type: "date", nullable: false),
                    surcharge_uah = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    acting_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acting_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_voucher_renewals", x => x.id);
                    table.ForeignKey(
                        name: "FK_operator_voucher_renewals_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operator_voucher_renewals_created_at_utc",
                table: "operator_voucher_renewals",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_operator_voucher_renewals_customer_user_id",
                table: "operator_voucher_renewals",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_operator_voucher_renewals_voucher_id",
                table: "operator_voucher_renewals",
                column: "voucher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operator_voucher_renewals");
        }
    }
}
