using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class DropVoucherExchangeInvoiceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoice_date",
                table: "voucher_exchanges");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                table: "voucher_exchanges");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "invoice_date",
                table: "voucher_exchanges",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                table: "voucher_exchanges",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
