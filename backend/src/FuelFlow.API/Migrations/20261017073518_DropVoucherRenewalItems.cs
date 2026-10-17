using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13, step 3 <b>contract</b>: <c>voucher_renewal_items</c> is gone.
    ///
    /// <para>
    /// The table repeated, row for row, facts the order line already carried. Nothing is moved here
    /// because nothing had to be: <c>source_voucher_id</c> and <c>term_code</c> arrived on
    /// <c>order_line_items</c> in <c>20261017063149</c>, the expiry snapshots in
    /// <c>20261017065731</c>, and <c>amount_paid</c> was provably the line's own
    /// <c>line_total</c> - quantity is 1 for a renewal, so <c>unit_price</c> equals it too. The only
    /// two columns with no counterpart on the line, <c>fulfilled_voucher_id</c> and
    /// <c>fulfilled_at_utc</c>, were never the line's business: they are the delivery, and
    /// <c>fulfillments</c> already records both per line.
    /// </para>
    ///
    /// <para>
    /// <b>What this removes is a second thing that had to be kept true.</b> Until now a renewal was
    /// two rows that agreed - the line said what was bought, the item said what was delivered - and
    /// the three steps of D-13 were needed to make them agree before they could disagree visibly.
    /// There is now one row per renewal from checkout to expiry, and no pairing to reconstruct: the
    /// thing that reads "which voucher did this renewal deliver" reads
    /// <c>fulfillments.line_item_id</c>, a real foreign key, not a positional guess.
    /// </para>
    ///
    /// <para>
    /// <b>The guard did not move, it changed owner.</b> This table's
    /// <c>FK_voucher_renewal_items_orders_order_id</c> RESTRICT was what stopped a paid renewal order
    /// from being deleted. That job now belongs to <c>order_line_items.order_id</c>, also RESTRICT,
    /// flipped in <c>20261017065731</c> precisely so it would be in place before this table went.
    /// <c>DeletedUnpaidOrderCleanupService</c> had already stopped asking the database permission by
    /// hand and started removing its own lines first.
    /// </para>
    ///
    /// <para>
    /// <b>No backfill question arises.</b> Production holds zero orders, so there are no renewal rows
    /// to carry across - and there was never anything to carry, since every column had a live owner
    /// before the drop.
    /// </para>
    ///
    /// <para>
    /// <b>This does not build the ledger.</b> D-13's end state is <c>voucher_movements</c>, which does
    /// not exist yet. This step only makes the duplication it would replace go away, so that the
    /// ledger is built on a schema with one renewal record rather than three movement tables and a
    /// spare one. Note for whoever builds it: <b>extend is not a movement.</b> Pushing an expiry moves
    /// no voucher, and <c>CHECK (from_voucher_id IS DISTINCT FROM to_voucher_id)</c> forbids writing it
    /// as one; it is a state change for <c>voucher_events</c>. A replace is two movements.
    /// </para>
    /// </summary>
    public partial class DropVoucherRenewalItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_renewal_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_renewal_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fulfilled_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    fulfilled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    line_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    new_customer_expiration = table.Column<DateOnly>(type: "date", nullable: true),
                    previous_customer_expiration = table.Column<DateOnly>(type: "date", nullable: true),
                    term_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_renewal_items", x => x.id);
                    table.CheckConstraint("ck_voucher_renewal_amount_non_negative", "amount_paid IS NULL OR amount_paid >= 0");
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_fuel_vouchers_fulfilled_voucher_id",
                        column: x => x.fulfilled_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_fuel_vouchers_source_voucher_id",
                        column: x => x.source_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_order_line_items_line_item_id",
                        column: x => x.line_item_id,
                        principalTable: "order_line_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_renewal_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_fulfilled_voucher_id",
                table: "voucher_renewal_items",
                column: "fulfilled_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_renewal_items_line_item_id",
                table: "voucher_renewal_items",
                column: "line_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_order_id",
                table: "voucher_renewal_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_voucher_renewal_items_source_voucher_id",
                table: "voucher_renewal_items",
                column: "source_voucher_id");
        }
    }
}
