using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
/// D-13: <c>voucher_movements</c> tracks <b>custody</b>, not paper lineage.
/// </summary>
/// <para>
/// <b>The previous shape asked two questions with two columns and could answer only one of them.</b>
/// It had <c>from_voucher_id NOT NULL</c> and <c>to_voucher_id</c>: which paper moved, and which paper
/// it became. The second is provenance, not movement. Every genuine handover out of the warehouse —
/// issuing a ticket to a customer, taking one back — therefore had to write
/// <c>to_voucher_id = NULL</c> and put the real destination in <c>order_id</c>. A column that is
/// empty on every real row is a column modelling the wrong thing, and the fix is not to choose a
/// different convention for the NULL.
/// </para>
/// <para>
/// So the paper is <c>voucher_id</c>, and the movement is <c>from_location</c> →
/// <c>to_location</c> over a five-word vocabulary the business already uses: pool, customer, supplier,
/// company, worker. Every row now says where the paper went, with no NULL to interpret.
/// </para>
/// <para>
/// <b>The kind set widens from four to eight</b>, and every one of the new words is a real change of
/// place: <c>issued</c> and <c>returned</c> (a customer's hands and back), <c>gift</c> and
/// <c>recall</c> (a company pool and a worker). The four that were already there — <c>deposit</c>,
/// <c>exchange_out</c>, <c>exchange_in</c>, <c>replace_out</c> — are the same events as before.
/// A redemption, a block, an activation and an expiry are still absent, and still correctly so: they
/// change what a ticket <i>is</i>, not where it <i>is</i>, and they belong to <c>voucher_events</c>.
/// </para>
/// <para>
/// <b><c>extend</c> no longer needs a constraint to stay out.</b> It moves no paper between places, so
/// it is not a movement by the meaning of the word rather than by
/// <c>ck_voucher_movements_distinct_sides</c> refusing <c>from = to</c> — a rule about vouchers
/// happening to be the same row on both sides, which was the wrong lever.
/// </para>
/// <para>
/// <b>Provenance stays derived.</b> "Which paper did this one come from" is answerable from an
/// <c>exchange_in</c> and the <c>exchange_out</c> sharing a batch, or a <c>replace_out</c> pointing at
/// the line that names the old paper. A <c>voucher_lineage</c> table would be a second copy of that
/// fact to keep in step, which is the defect D-13 exists to remove.
/// </para>
/// <para>
/// No data is affected: nothing writes this table yet.
/// </para>
/// </summary>
public partial class VoucherMovementsTrackCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_from_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_to_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_from",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_to",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_distinct_sides",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_has_a_side",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "from_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "to_voucher_id",
                table: "voucher_movements");

            migrationBuilder.AddColumn<string>(
                name: "from_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "to_location",
                table: "voucher_movements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_order",
                table: "voucher_movements",
                column: "order_id",
                filter: "order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_voucher",
                table: "voucher_movements",
                columns: new[] { "voucher_id", "occurred_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_distinct_locations",
                table: "voucher_movements",
                sql: "from_location <> to_location");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements",
                sql: "kind IN ('deposit','issued','returned','replace_out','exchange_out','exchange_in','gift','recall')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements",
                sql: "from_location IN ('pool','customer','supplier','company','worker') AND to_location IN ('pool','customer','supplier','company','worker')");

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_voucher_id",
                table: "voucher_movements",
                column: "voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_voucher_id",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_order",
                table: "voucher_movements");

            migrationBuilder.DropIndex(
                name: "ix_voucher_movements_voucher",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_distinct_locations",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_voucher_movements_locations",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "from_location",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "to_location",
                table: "voucher_movements");

            migrationBuilder.DropColumn(
                name: "voucher_id",
                table: "voucher_movements");

            migrationBuilder.AddColumn<Guid>(
                name: "from_voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "to_voucher_id",
                table: "voucher_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_from",
                table: "voucher_movements",
                columns: new[] { "from_voucher_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_movements_to",
                table: "voucher_movements",
                column: "to_voucher_id",
                filter: "to_voucher_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_distinct_sides",
                table: "voucher_movements",
                sql: "from_voucher_id IS DISTINCT FROM to_voucher_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_has_a_side",
                table: "voucher_movements",
                sql: "from_voucher_id IS NOT NULL OR to_voucher_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_voucher_movements_kind",
                table: "voucher_movements",
                sql: "kind IN ('deposit','activate','deactivate','archive','unarchive','issuance','redeem','unredeem','exchange_out','exchange_in','replace_out','restock','gift','recall','block','unblock','fire_worker','expire')");

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_from_voucher_id",
                table: "voucher_movements",
                column: "from_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_voucher_movements_fuel_vouchers_to_voucher_id",
                table: "voucher_movements",
                column: "to_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
