using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AttributeFulfillmentsToOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "line_item_id",
                table: "voucher_renewal_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "line_item_id",
                table: "fulfillments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_renewal_items_line_item_id",
                table: "voucher_renewal_items",
                column: "line_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_line_item_id",
                table: "fulfillments",
                column: "line_item_id",
                filter: "released_at_utc IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_fulfillments_order_line_items_line_item_id",
                table: "fulfillments",
                column: "line_item_id",
                principalTable: "order_line_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_renewal_items_order_line_items_line_item_id",
                table: "voucher_renewal_items",
                column: "line_item_id",
                principalTable: "order_line_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---------------------------------------------------------------- backfill
            //
            // Both columns stay nullable afterwards, and that is the honest state: a fulfilment that
            // matched exactly one line of its own order is attributed here, and one that matched none
            // or several is left null rather than guessed at. The reconciliation still handles null
            // (it falls back to the litres comparison), so a partial backfill improves the orders it
            // can without making the ones it cannot any worse.
            //
            // Matching is on (brand, fuel_type_id, liters) case-insensitively, because that is the
            // only thing a voucher and a line item share - a voucher does not know what position it
            // was bought in. `brand` is compared lower() on both sides: the column is written from
            // supplier data and from request paths that differ in case, and the fulfilment query
            // itself compares it that way.
            //
            // Written as raw SQL rather than as a second scaffolding step, for the same reason
            // 20260919113317_AddMissingDriftedTables is: this is data, not a schema difference, and
            // regenerating it from the model would silently drop it.
            //
            // Three passes, in this order, because each is strictly more accurate than the next:
            //   1. a renewal item to the line it charges - mirrors the source voucher
            //   2. a renewal fulfilment to the line its own renewal item named - exact, not inferred
            //   3. everything else by (brand, fuel, litres), only where that matches exactly one line
            migrationBuilder.Sql("""
                UPDATE voucher_renewal_items vri
                   SET line_item_id = m.line_item_id
                  FROM (
                        SELECT vri2.id            AS renewal_item_id,
                               li.id            AS line_item_id
                          FROM voucher_renewal_items vri2
                          JOIN orders o               ON o.id = vri2.order_id
                          JOIN fuel_vouchers src      ON src.id = vri2.source_voucher_id
                          JOIN order_line_items li    ON li.order_id = o.id
                                                 AND lower(li.brand)        = lower(src.brand)
                                                 AND lower(li.fuel_type_id) = lower(src.fuel_type_id)
                                                 AND li.liters             = src.liters
                       WHERE vri2.line_item_id IS NULL
                       -- Only where the match is unique. Two renewal lines on the same fuel and
                       -- volume are indistinguishable after the fact, and picking one would invent a
                       -- correspondence that may be wrong.
                         AND (SELECT count(*) FROM order_line_items li2
                                WHERE li2.order_id = o.id
                                  AND lower(li2.brand)        = lower(src.brand)
                                  AND lower(li2.fuel_type_id) = lower(src.fuel_type_id)
                                  AND li2.liters             = src.liters) = 1
                       -- ...and it is the right line: a renewal line is always one unit.
                         AND li.quantity = 1
                   ) m
                 WHERE vri.id = m.renewal_item_id;
                """);

            // Renewal fulfillments follow the renewal item rather than guessing: the item already
            // named a line, and re-deriving it from the voucher would lose that. This is the pass
            // that would otherwise be least accurate, because a renewal order's lines mirror their
            // source vouchers and say nothing about which one a given fulfilment belongs to.
            migrationBuilder.Sql("""
                UPDATE fulfillments f
                   SET line_item_id = vri.line_item_id
                  FROM voucher_renewal_items vri
                 WHERE vri.fulfilled_voucher_id = f.voucher_id
                   AND vri.order_id = f.order_id
                   AND vri.line_item_id IS NOT NULL
                   AND f.line_item_id IS NULL;
                """);

            // Buy-fuel, plus any renewal the pass above could not resolve. A voucher knows its fuel
            // and its volume and nothing else, so this is as close as history allows.
            migrationBuilder.Sql("""
                UPDATE fulfillments f
                   SET line_item_id = m.line_item_id
                  FROM (
                        SELECT f2.id           AS fulfillment_id,
                               li.id           AS line_item_id
                          FROM fulfillments f2
                          JOIN fuel_vouchers v        ON v.id = f2.voucher_id
                          JOIN order_line_items li     ON li.order_id = f2.order_id
                                                  AND lower(li.brand)        = lower(v.brand)
                                                  AND lower(li.fuel_type_id) = lower(v.fuel_type_id)
                                                  AND li.liters             = v.liters
                       WHERE f2.line_item_id IS NULL
                         AND (SELECT count(*) FROM order_line_items li2
                                WHERE li2.order_id = f2.order_id
                                  AND lower(li2.brand)        = lower(v.brand)
                                  AND lower(li2.fuel_type_id) = lower(v.fuel_type_id)
                                  AND li2.liters             = v.liters) = 1
                   ) m
                 WHERE f.id = m.fulfillment_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfill is not reversed on purpose. An order whose fulfilments were attributed by
            // inference would, on the way down, leave some rows attributed and some not - and the
            // reconciliation reads that difference as a real mismatch, which is precisely the failure
            // this column exists to stop. Clearing both columns instead is idempotent and leaves no
            // half-attributed order behind.
            migrationBuilder.Sql("UPDATE fulfillments SET line_item_id = NULL;");
            migrationBuilder.Sql("UPDATE voucher_renewal_items SET line_item_id = NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_fulfillments_order_line_items_line_item_id",
                table: "fulfillments");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_renewal_items_order_line_items_line_item_id",
                table: "voucher_renewal_items");

            migrationBuilder.DropIndex(
                name: "ix_voucher_renewal_items_line_item_id",
                table: "voucher_renewal_items");

            migrationBuilder.DropIndex(
                name: "ix_fulfillments_line_item_id",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "line_item_id",
                table: "voucher_renewal_items");

            migrationBuilder.DropColumn(
                name: "line_item_id",
                table: "fulfillments");
        }
    }
}
