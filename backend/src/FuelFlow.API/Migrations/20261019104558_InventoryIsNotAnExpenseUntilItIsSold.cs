using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class InventoryIsNotAnExpenseUntilItIsSold : Migration
    {
/// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The mapping said cost happens TWICE: once when the stock is bought (stock.purchased,
            // Dr 808 / Cr 281) and again when the voucher is redeemed (voucher.redeemed.cogs,
            // Dr 808 / Cr 281). Both debit the same expense account and credit the same stock
            // account, so a voucher that was bought and then redeemed would cost twice and would
            // leave 281 negative - the shelf would hold less than zero litres.
            //
            // The business chose expense-at-sale on 2026-10-19, which is also what IAS 2 asks for:
            // inventories are recognised as an expense when they are sold, not when they are
            // bought. Buying stock moves value onto the shelf and raises a payable; it is not an
            // expense. So the buy-side entry is stock.purchased.payable, which was already correct,
            // and stock.purchased was the redundant one.
            //
            // Deleted rather than re-pointed because "stock.purchased" is the wrong *name* for
            // Dr 281 / Cr 631 - nothing about it is payable - and leaving an event mapped to the
            // account that used to hold the bug is how it comes back.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules
                WHERE source_event = 'stock.purchased';
                """);

            // Cash purchase is the other half of the same fact and had no mapping at all: bought
            // and paid on the spot is Dr 281 / Cr 311. Without it, only the credit purchase could
            // be recorded, so the obvious case - pay the supplier immediately - would have been
            // impossible to book.
            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), 'Cost', 'stock.purchased.cash', a.id, v.side
                FROM (VALUES ('281', 'Debit'), ('311', 'Credit')) AS v(code, side)
                JOIN chart_of_accounts a ON a.code = v.code
                WHERE NOT EXISTS (
                    SELECT 1 FROM ledger_mapping_rules WHERE source_event = 'stock.purchased.cash');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restoring the bug is not offered. Down removes the cash mapping and leaves the
            // double-count deleted: a rollback that put the duplicate expense back would re-arm a
            // defect the moment it ran, and there is no operational reason to prefer that state.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules
                WHERE source_event = 'stock.purchased.cash';
                """);
        }
    }
}
