using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives a returned advance a VAT leg: <c>payment.refunded.vat</c>, Dr 643 / Cr 704.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>payment.received.vat</c> carves the VAT out of every payment when the money arrives — Dr 704
    /// («Вирахування з доходу») / Cr 643 (planning #787, D2 in <c>docs/LEDGER_CLOSE_2026-10.md</c>).
    /// Nothing released it when part of that money went back: the refund was booked gross against 681
    /// and 311, so the liability to the budget stayed at the figure a sale that no longer happened
    /// would have owed. The first real refund (2026-11-02, 862.00 ₴ of a 1724.00 ₴ order) leaves 143.67
    /// ₴ too much on 643 (planning #854, #857).
    /// </para>
    /// <para>
    /// <b>The ruling is the business's, and it was given on 2026-11-03:</b> a returned advance releases
    /// its VAT in the same period. That is why this arrives as two rows rather than as code — the
    /// engine resolves accounts from <c>ledger_mapping_rules</c> and no account name lives in a class,
    /// so a bookkeeping decision is a data change. Had the ruling been "only once the supply
    /// adjustment is documented", the answer would have been an operator entry and no rule at all.
    /// </para>
    /// <para>
    /// <b>The pair is the exact mirror of the receipt.</b> 643 is a liability, so releasing it is a
    /// debit; 704 is the deduction taken against revenue, and returning the sale returns the deduction.
    /// The sides are declared per leg, not derived from the accounts' <c>normal_balance</c>: a posting
    /// moves an account whichever way its own balance runs.
    /// </para>
    /// </remarks>
    public partial class TheReturnedAdvanceReleasesItsVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded on the event rather than the row: a second pair of rules for one event would
            // make the resolution ambiguous, and the engine reads the event alone.
            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), 'Payment'::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    ('payment.refunded.vat', '643', 'Debit'),
                    ('payment.refunded.vat', '704', 'Credit')
                ) AS m(source_event, code, side)
                JOIN chart_of_accounts a ON a.code = m.code
                WHERE NOT EXISTS (
                    SELECT 1 FROM ledger_mapping_rules r WHERE r.source_event = m.source_event);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Entries already posted under the event keep their accounts — journal_lines reference the
            // chart, not the rule — so removing the rule stops future postings and rewrites no history.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules WHERE source_event = 'payment.refunded.vat';
                """);
        }
    }
}
