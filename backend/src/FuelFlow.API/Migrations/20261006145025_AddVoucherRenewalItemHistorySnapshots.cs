using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherRenewalItemHistorySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_paid",
                table: "voucher_renewal_items",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "voucher_renewal_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "voucher_renewal_items",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amount_paid",
                table: "voucher_renewal_items");

            migrationBuilder.DropColumn(
                name: "new_customer_expiration",
                table: "voucher_renewal_items");

            migrationBuilder.DropColumn(
                name: "previous_customer_expiration",
                table: "voucher_renewal_items");
        }
    }
}
