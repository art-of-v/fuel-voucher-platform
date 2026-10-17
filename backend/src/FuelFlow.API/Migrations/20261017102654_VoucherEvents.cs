using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
/// D-13: <c>voucher_events</c>, the state history that the custody ledger deliberately does not carry.
/// </summary>
/// <para>
/// <b>Why this is a separate table rather than more columns on <c>voucher_movements</c>.</b> Almost
/// nothing in this system moves a voucher: one <c>fuel_vouchers</c> row is updated in place for its
/// whole life. Activation, a redemption, a block, handing a ticket over and taking it back are all that
/// same row with different columns set. The ledger answers "where is this paper"; this answers "what was
/// done to it". Neither derives the other, so each fact has one owner.
/// </para>
/// <para>
/// <b>Five event types, not one per action.</b> The nine <c>FuelVoucher.Status</c> values are already a
/// closed set, so a <c>StatusChanged</c> row carries <c>from_status</c>/<c>to_status</c> and activation,
/// redemption, block and expiry are all the same kind of thing. A per-action vocabulary grows a word per
/// action and then nobody uses them — <c>order_events</c> has eight such types and writes two.
/// </para>
/// <para>
/// <b><c>Extended</c> is here, and it is the reason the table exists.</b> A paid renewal pushes
/// <c>fuel_vouchers.customer_expiration_date</c> out by overwriting it, so "what was it before" is gone
/// unless it is written down while it is still true. The pair is CHECK-pinned as whole and forward-only,
/// because a half-written expiry history is not recoverable by any later reader — the same defect the
/// order-line renewal snapshots (<c>20261017065731</c>) prevent, checked the same way.
/// </para>
/// <para>
/// <b>No partitioning, on purpose.</b> TARGET_SCHEMA sketches this table as
/// <c>PARTITION BY RANGE (occurred_at_utc)</c>, and a partitioned table needs a job that creates next
/// month's partition — without one it starts refusing inserts. That job does not exist, so partitioning
/// now would be a table that works for a while and then stops.
/// </para>
/// <para>
/// <b>No backfill.</b> Production holds no voucher movement history, and nothing here is derivable
/// backwards anyway: a voucher's current status and expiry are single overwritten values, so the
/// transitions that led to them are already gone.
/// </para>
/// </summary>
    public partial class VoucherEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "voucher_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    previous_customer_expiration = table.Column<DateOnly>(type: "date", nullable: true),
                    new_customer_expiration = table.Column<DateOnly>(type: "date", nullable: true),
                    external_ref = table.Column<string>(type: "text", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_party_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voucher_events", x => x.id);
                    table.CheckConstraint("ck_voucher_events_expiration_pair", "(event_type <> 'Extended') = (previous_customer_expiration IS NULL AND new_customer_expiration IS NULL) AND (event_type <> 'Extended' OR new_customer_expiration > previous_customer_expiration)");
                    table.CheckConstraint("ck_voucher_events_imported_has_no_predecessor", "event_type <> 'Imported' OR (from_status IS NULL AND previous_customer_expiration IS NULL)");
                    table.CheckConstraint("ck_voucher_events_status_changed", "(event_type <> 'StatusChanged') = (from_status IS NULL AND to_status IS NULL) AND (event_type <> 'StatusChanged' OR from_status <> to_status)");
                    table.CheckConstraint("ck_voucher_events_statuses", "(from_status IS NULL OR from_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated')) AND (to_status IS NULL OR to_status IN ('Imported','VerifiedWithWarnings','VerificationFailed','Available','Assigned','Used','Expired','Blocked','Deactivated'))");
                    table.CheckConstraint("ck_voucher_events_type", "event_type IN ('Imported','StatusChanged','Extended','Archived','Unarchived')");
                    table.ForeignKey(
                        name: "FK_voucher_events_fuel_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "fuel_vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_ref",
                table: "voucher_events",
                column: "external_ref",
                filter: "external_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_voucher",
                table: "voucher_events",
                columns: new[] { "voucher_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_events");
        }
    }
}
