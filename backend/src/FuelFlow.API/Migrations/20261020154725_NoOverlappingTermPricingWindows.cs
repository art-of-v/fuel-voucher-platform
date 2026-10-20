using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The price windows of one tier may not overlap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table carried two protections and neither of them said what "no overlap" means. The
    /// partial unique index (<c>ux_voucher_term_pricing_open_tier</c>) pins ONE OPEN row per
    /// (scheme, term_code), and the CHECK pins that an end is after a start. Two <b>closed</b>
    /// windows for the same tier still satisfied both: <c>[2026-01-01, 2026-06-01)</c> and
    /// <c>[2026-03-01, 2026-09-01)</c> are each a valid window, and together they leave the
    /// lookup with two candidate rows for the same date - so the answer depended on which one the
    /// query happened to pick.
    /// </para>
    /// <para>
    /// The exclusion constraint is the instrument that says what the question means. It is checked
    /// on every insert and every update, so the overlap cannot be created in the first place -
    /// which is much cheaper now than after the first overlapping price has been inserted and
    /// silently picked.
    /// </para>
    /// <para>
    /// <b>btree_gist</b> gives GiST the equality operator for the two text columns; without it
    /// Postgres cannot build this constraint over <c>scheme</c> and <c>term_code</c>. The
    /// extension is left in place by the Down migration: it is a shared extension, and dropping it
    /// would take away an operator class another constraint may be using.
    /// </para>
    /// <para>
    /// The range is <c>tstzrange(valid_from, valid_to)</c> - the columns are
    /// <c>timestamp with time zone</c>, and the default bounds are <c>[)</c>, which is the
    /// half-open window the CHECK already describes: a window that ends exactly where the next one
    /// begins touches it without overlapping it. A row with no end is an open window, and it
    /// overlaps everything that comes after it - which is the point.
    /// </para>
    /// <para>
    /// The rows in production satisfy this: the price history is a chain of windows that meet
    /// end-to-end, and the migration's own check for overlapping pairs found none.
    /// </para>
    /// </remarks>
    public partial class NoOverlappingTermPricingWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS btree_gist;""");

            migrationBuilder.Sql(
                """
                ALTER TABLE "voucher_term_pricing"
                ADD CONSTRAINT "ex_voucher_term_pricing_no_overlap"
                EXCLUDE USING gist (
                    "scheme" WITH =,
                    "term_code" WITH =,
                    tstzrange("valid_from", "valid_to") WITH &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "voucher_term_pricing" DROP CONSTRAINT IF EXISTS "ex_voucher_term_pricing_no_overlap";""");
        }
    }
}
