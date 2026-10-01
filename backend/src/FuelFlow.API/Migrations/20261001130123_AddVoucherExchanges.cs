using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_exchanges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fuel_type_id = table.Column<string>(type: "text", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    surcharge_uah = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    cost_per_liter_applied = table.Column<decimal>(type: "numeric(10,4)", nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    acting_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acting_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_exchanges", x => x.id);
                    table.ForeignKey(
                        name: "FK_voucher_exchanges_fuel_vouchers_old_voucher_id",
                        column: x => x.old_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_exchange_batch_id",
                table: "voucher_exchanges",
                column: "exchange_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_new_voucher_id",
                table: "voucher_exchanges",
                column: "new_voucher_id");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_exchanges_old_voucher_id",
                table: "voucher_exchanges",
                column: "old_voucher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_exchanges");
        }
    }
}
