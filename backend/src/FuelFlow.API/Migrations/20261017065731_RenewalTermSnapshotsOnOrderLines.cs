using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13, step 2 <b>expand</b>: the renewal's expiry snapshots move onto the order line.
    ///
    /// <para>
    /// #1180 put the source voucher, the term and the branch on <c>order_line_items</c>. These are the
    /// last two facts of a renewal that lived anywhere else, and they are the reason the table had to
    /// exist at all: <c>previous_customer_expiration</c> is a snapshot of a value that
    /// <c>TryExtendVoucherAsync</c> overwrites in place, so once the renewal is applied there is
    /// nothing left to read it from anywhere but here.
    /// </para>
    ///
    /// <para>
    /// <b>This is the expand half of expand/contract, on purpose.</b> <c>voucher_renewal_items</c> stays,
    /// and stays authoritative for everything else, so the two copies of the snapshots are written
    /// together from the same two values in the same loop iteration and cannot drift. Dropping the old
    /// columns and the table is a separate PR that touches seven readers and the fixtures for all of
    /// them; mixing that with the schema change here would make neither reviewable.
    /// </para>
    ///
    /// <para>
    /// <b>No backfill, and nothing is lost by that.</b> Production holds zero orders, so there are no
    /// renewal rows to copy across; and the values are not reconstructible anyway — they depend on the
    /// voucher's state on the day the renewal was applied, which no longer exists anywhere. A future
    /// contract step reading the old columns for anything would find them null, which is the truth
    /// about this database and the honest answer for a pre-launch one.
    /// </para>
    ///
    /// <para>
    /// <b>The FK flip is the part that has to happen here.</b> <c>order_line_items.order_id</c> was
    /// CASCADE — the one place in the schema where a money line cascaded away with its order. When the
    /// renewal table is dropped, this is the constraint that will be left stopping an order a customer
    /// paid a renewal on from being deleted. Turning it now means the guard is structural from the
    /// start of the contract step rather than briefly absent in the middle of it.
    /// </para>
    /// </summary>
    public partial class RenewalTermSnapshotsOnOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items");

            migrationBuilder.AddColumn<DateOnly>(
                name: "new_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "previous_customer_expiration",
                table: "order_line_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_expirations_ordered",
                table: "order_line_items",
                sql: "previous_customer_expiration IS NULL OR new_customer_expiration IS NULL OR new_customer_expiration > previous_customer_expiration");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_snapshots_are_paired",
                table: "order_line_items",
                sql: "(previous_customer_expiration IS NULL) = (new_customer_expiration IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_expirations_ordered",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_snapshots_are_paired",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "new_customer_expiration",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "previous_customer_expiration",
                table: "order_line_items");

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_orders_order_id",
                table: "order_line_items",
                column: "order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
