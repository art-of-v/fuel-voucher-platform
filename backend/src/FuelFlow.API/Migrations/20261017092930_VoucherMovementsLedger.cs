using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13, stage 1 <b>expand</b>: the ledger exists, and nothing writes it yet.
    /// </summary>
    /// <para>
    /// The table the three movement tables fold into: <c>voucher_renewal_items</c> is already gone
    /// (<c>20261017073518</c>), <c>voucher_exchanges</c> and <c>operator_voucher_renewals</c> still
    /// stand and are read by five call sites and none respectively. This stage adds the destination and
    /// nothing else, because the writers are the risky half and a half-built ledger is worse than an
    /// empty one.
    /// </para>
    /// <para>
    /// <b>No backfill, and nothing to backfill.</b> Production holds zero orders, so there is no movement
    /// history to copy. More to the point there is nothing to copy <i>to</i>: a movement's
    /// <c>cost_delta_uah</c> is by definition unrecoverable from the live schema, because
    /// <c>fuel_vouchers.cost_per_liter</c> is mutable and <c>SetBatchCostCommandHandler</c> restamps it.
    /// A backfill that derived the delta from today's column would write a confidently wrong number
    /// into an append-only table, which is the one thing an append-only table must never contain.
    /// </para>
    /// <para>
    /// <b>Three columns differ from the first TARGET_SCHEMA draft, each for a reason that was checked
    /// against the shipped schema rather than assumed.</b> <c>from_voucher_id</c> is nullable because
    /// <c>ImportVouchersCommand</c> creates the majority of vouchers in this system and a deposit has
    /// no predecessor — under NOT NULL the largest class of movement was unrepresentable.
    /// <c>order_id</c>, <c>actor_user_id</c> and <c>actor_party_id</c> carry no foreign keys: an audit
    /// row must not block deleting its own subject, which is precisely how
    /// <c>order_line_items.order_id</c> being RESTRICT wedged <c>DeletedUnpaidOrderCleanupService</c>,
    /// and the actor rule is §G of TARGET_SCHEMA as four tables in this schema already follow it. The
    /// draft's <c>actor_party_id REFERENCES parties(id)</c> also pointed at a table that does not exist
    /// (D-02), and its <c>supplier_party_id REFERENCES legal_entities(party_id)</c> at a column that
    /// does not exist.
    /// </para>
    /// <para>
    /// <b>Extend has no kind.</b> Pushing an expiry out moves no voucher, and
    /// <c>ck_voucher_movements_distinct_sides</c> is what makes that unrepresentable rather than merely
    /// discouraged. It is a <c>voucher_events</c> state change; the money is already an
    /// <c>orders</c>/<c>payments</c> fact against the renewal line.
    /// </para>
    /// <para>
    /// <b>Writing it comes next, and it has one hard rule.</b> <c>fuel_vouchers</c> carries an
    /// <c>xmin</c> token and eleven call sites mutate it with raw SQL compare-and-swap, every one of
    /// which treats rows-affected == 0 as "someone beat us". A movement may therefore only be written
    /// when the statement it describes actually changed a row — otherwise a Hangfire retry or two
    /// concurrent workers records movements for vouchers that did not move.
    /// </para>
    /// </summary>
    public partial class VoucherMovementsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    from_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_uah = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    cost_delta_uah = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    line_item_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_movements", x => x.id);
                    table.CheckConstraint("ck_voucher_movements_amount_non_negative", "amount_uah IS NULL OR amount_uah >= 0");
                    table.CheckConstraint("ck_voucher_movements_distinct_sides", "from_voucher_id IS DISTINCT FROM to_voucher_id");
                    table.CheckConstraint("ck_voucher_movements_has_a_side", "from_voucher_id IS NOT NULL OR to_voucher_id IS NOT NULL");
                    table.CheckConstraint("ck_voucher_movements_kind", "kind IN ('deposit','activate','deactivate','archive','unarchive','issuance','redeem','unredeem','exchange_out','exchange_in','replace_out','restock','gift','recall','block','unblock','fire_worker','expire')");
                    table.ForeignKey(
                        name: "FK_voucher_movements_fuel_vouchers_from_voucher_id",
                        column: x => x.from_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_movements_fuel_vouchers_to_voucher_id",
                        column: x => x.to_voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_voucher_movements_movement_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "movement_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_batch",
                table: "voucher_movements",
                column: "batch_id",
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_from",
                table: "voucher_movements",
                columns: new[] { "from_voucher_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_line",
                table: "voucher_movements",
                column: "line_item_id",
                filter: "line_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_to",
                table: "voucher_movements",
                column: "to_voucher_id",
                filter: "to_voucher_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_movements");
        }
    }
}
