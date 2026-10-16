using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class PaymentConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the token. `ux_payments_one_refund_per_order` lands with the view migration above,
            // because it is part of the same change of shape - the guarantee it carries
            // (`refunds.order_id` was UNIQUE) is inherited by the index, and naming this migration
            // after it too would send a reader looking for DDL that is one file away.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "payments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "payments");
        }
    }
}
