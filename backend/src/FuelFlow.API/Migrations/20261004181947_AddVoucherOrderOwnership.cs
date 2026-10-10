using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherOrderOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Purchase");

            migrationBuilder.AddColumn<Guid>(
                name: "source_order_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            // Backfill, not guesswork: fulfillments has been the order<->voucher link all along, so
            // the owning order of every voucher ever delivered can be recovered exactly.
            //
            // fulfillments.voucher_id is NOT unique, so a plain UPDATE ... FROM would pick an
            // arbitrary row for vouchers with more than one fulfillment. DISTINCT ON with an
            // explicit ordering makes it deterministic: the earliest fulfillment is the one that
            // originally delivered the voucher, which for a renewed voucher is the purchase rather
            // than the later renewal.
            migrationBuilder.Sql(
                """
                UPDATE "fuel_vouchers" AS v
                SET "order_id" = f."order_id"
                FROM (
                    SELECT DISTINCT ON ("voucher_id") "voucher_id", "order_id"
                    FROM "fulfillments"
                    ORDER BY "voucher_id", "fulfilled_at_utc", "order_id"
                ) AS f
                WHERE f."voucher_id" = v."id" AND v."order_id" IS NULL
                """);

            // kind defaults to Purchase, but an order carrying renewal items is a renewal - that is
            // exactly how the webhook and the payment simulator already recognise one
            // (VoucherRenewalItems.AnyAsync(i => i.OrderId == order.Id)). Label them now, while
            // nothing reads the column yet, so the data is honest the day the money views start
            // filtering on it.
            migrationBuilder.Sql(
                """
                UPDATE "orders" AS o
                SET "kind" = 'Renewal'
                WHERE o."kind" = 'Purchase'
                  AND EXISTS (SELECT 1 FROM "voucher_renewal_items" i WHERE i."order_id" = o."id")
                """);

            migrationBuilder.CreateIndex(
                name: "IX_orders_source_order_id",
                table: "orders",
                column: "source_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_order_id",
                table: "fuel_vouchers",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_orders_order_id",
                table: "fuel_vouchers",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_orders_orders_source_order_id",
                table: "orders",
                column: "source_order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_orders_order_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_orders_source_order_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_source_order_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_order_id",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source_order_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "fuel_vouchers");
        }
    }
}
