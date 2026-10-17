using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// D-13 stage 2, step 1 of 3: put the renewal INTENT on the order line it describes.
    ///
    /// <para>
    /// <b>The gap this closes.</b> <c>RenewalCheckoutCommandHandler</c> wrote the term, the source
    /// voucher and the chosen branch to <c>voucher_renewal_items</c>, and created
    /// <c>order_line_items</c> without any of them. So the line the customer was charged for could not
    /// say which voucher it renews, for what term, or by which path — and the branch had to be
    /// reconstructed from a table with no relationship to the line.
    /// </para>
    ///
    /// <para>
    /// <b>No backfill, and that is the point rather than an omission.</b> Every renewal line written
    /// before this migration has a null <c>term_code</c>, and the term is not reconstructible after the
    /// fact: the branch was decided from the voucher's state that day, and the voucher's
    /// <c>customer_expiration_date</c> has since been overwritten in place by
    /// <c>TryExtendVoucherAsync</c>. Backfilling today's state would manufacture a history that never
    /// happened, which is worse than an honest gap — it looks like evidence.
    /// </para>
    ///
    /// <para>
    /// <b>The constraints apply to new rows only, by construction.</b> All three read
    /// <c>renewal_branch IS NULL OR …</c>, and every existing line has a null branch, so adding them
    /// validates against a live table without touching a row. From here a renewal line cannot be
    /// created half-formed again — which is exactly the state this migration exists because nothing
    /// prevented it.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing reads these columns yet.</b> <c>voucher_renewal_items</c> is still the authority and
    /// is still written. Steps 2 and 3 add the expiry snapshots and then make the old table a view, so
    /// no reader changes and no rollback has to carry a data migration. Additive in the meantime, so
    /// this step is independently revertible.
    /// </para>
    /// </summary>
    public partial class RenewalIntentOnOrderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "renewal_branch",
                table: "order_line_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_voucher_id",
                table: "order_line_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_line_items_source_voucher_id",
                table: "order_line_items",
                column: "source_voucher_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items",
                sql: "renewal_branch IS NULL OR renewal_branch IN ('extend','replace')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_has_term",
                table: "order_line_items",
                sql: "renewal_branch IS NULL OR term_code IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_line_renewal_pair_is_whole",
                table: "order_line_items",
                sql: "(renewal_branch IS NULL) = (source_voucher_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_fuel_vouchers_source_voucher_id",
                table: "order_line_items",
                column: "source_voucher_id",
                principalTable: "fuel_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_fuel_vouchers_source_voucher_id",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "ix_order_line_items_source_voucher_id",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_branch",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_has_term",
                table: "order_line_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_line_renewal_pair_is_whole",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "renewal_branch",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "source_voucher_id",
                table: "order_line_items");
        }
    }
}
