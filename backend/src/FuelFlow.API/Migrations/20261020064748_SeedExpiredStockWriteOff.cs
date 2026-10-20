using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives expired unsold stock a place to land in the ledger.
    /// <para>
    /// Until now, the nightly expiry sweep retired a voucher to <c>Expired</c> and recorded the
    /// loss in the per-batch P&amp;L - and the ledger carried nothing. Account 281 (Товари на складі)
    /// still held the voucher's cost, so the one book an accountant reads said the stock was there
    /// while the stock had been written off. Two books, two answers.
    /// </para>
    /// <para>
    /// The write-off is Dr 807 (Витрати від утрати товарів) / Cr 281 (Товари на складі): the
    /// voucher is stock that was lost, so its cost leaves the inventory account and lands on the
    /// element the Ukrainian Plan of Accounts gives for lost goods. 807 rather than 808
    /// (Витрати товарів, the cost of goods sold) because 808 is what a SALE books, and a voucher
    /// nobody bought is not a sale - the two would be indistinguishable in the accounts, and the
    /// whole point of booking the loss separately is that it can be read separately.
    /// </para>
    /// <para>
    /// <b>Only the accounts this business uses are seeded</b> - the same rule the chart's own
    /// migration states. 807 is added because this business now uses it, and the accountant's own
    /// chart replaces all of this when the accountant exists.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class SeedExpiredStockWriteOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A subaccount of 80, so it rolls up with the cost of goods sold but stays readable on
            // its own - "how much did we lose to expired stock" is a question the account can answer
            // without a report.
            migrationBuilder.Sql("""
                INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, parent_id, is_postable, is_active)
                SELECT gen_random_uuid(), '807', 'Витрати від утрати товарів', 'Expense', 'Debit', h.id, true, true
                FROM chart_of_accounts h
                WHERE h.code = '80';
                """);

            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), m.source_type::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    ('Cost', 'stock.expired.writeoff', '807', 'Debit'),
                    ('Cost', 'stock.expired.writeoff', '281', 'Credit')
                ) AS m(source_type, source_event, code, side)
                JOIN chart_of_accounts a ON a.code = m.code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // By event, not by account: an accountant who has since re-pointed the rule would leave
            // rows behind if this went by account.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules
                WHERE source_event = 'stock.expired.writeoff';
                """);

            migrationBuilder.Sql("""
                DELETE FROM chart_of_accounts
                WHERE code = '807';
                """);
        }
    }
}
