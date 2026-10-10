using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherRenewalItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_renewal_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    fulfilled_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fulfilled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_renewal_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_fuel_vouchers_source_voucher_id",
                        column: x => x.source_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_order_id",
                table: "voucher_renewal_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_source_voucher_id",
                table: "voucher_renewal_items",
                column: "source_voucher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_renewal_items");
        }
    }
}
