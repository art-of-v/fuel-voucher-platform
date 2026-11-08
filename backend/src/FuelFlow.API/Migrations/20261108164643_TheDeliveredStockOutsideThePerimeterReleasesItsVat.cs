using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheDeliveredStockOutsideThePerimeterReleasesItsVat : Migration
    {
        /// <summary>
        /// Maps <c>payment.received.vat.out_of_perimeter</c> — Dr 643 / Cr 704 — the release of the
        /// VAT the receipt carved out of a sale delivered from stock acquired outside the perimeter.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>payment.received.vat</c> carves the VAT out of every payment when the money arrives, and
        /// at that moment the route the fuel will be delivered on is genuinely not known: no order line
        /// names a supplier, and stock is picked by expiry date (<c>docs/ACQUISITION_CHANNELS.md</c>
        /// §2 and §3). So the receipt books the whole payment inside the perimeter, and every sale
        /// delivered from the route that sits outside it has been owing output VAT on a supply that has
        /// no taxable event behind it. The route's own rule — a voucher acquired outside the perimeter
        /// "must not reach a VAT account" — had no writer that could honour it, because the fact that
        /// decides it only exists after the money was booked.
        /// </para>
        /// <para>
        /// <b>The pair is the exact mirror of <c>payment.refunded.vat</c></b>
        /// (<c>20261103012924</c>), which releases the carve when the advance comes back: 643 is a
        /// liability, so releasing it is a debit, and 704 («Вирахування з доходу») is where the income
        /// deduction goes back (<c>docs/LEDGER_CLOSE_2026-10.md</c> D2 and D3). The carve itself stays
        /// at the receipt — D2 fixes the split there and the tax point is the payment, not the fuel —
        /// so this adds no new account and moves no existing rule; it is the second half of one fact
        /// the receipt could only state once.
        /// </para>
        /// <para>
        /// <b>What it does not do.</b> It does not touch the VAT already standing on 643 for sales
        /// delivered before this ran: that overstatement is real, it is visible in the account, and
        /// correcting history is the accountant's сторно decision, not a migration's.
        /// </para>
        /// </remarks>
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded on the event rather than the row: two pairs of rules for one event would make
            // the resolution ambiguous, and the engine reads the event alone.
            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), 'Payment'::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    ('payment.received.vat.out_of_perimeter', '643', 'Debit'),
                    ('payment.received.vat.out_of_perimeter', '704', 'Credit')
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
            // With no rule the posting now fails loudly, which is the correct answer: the release is
            // the only thing standing between 643 and an overstated liability.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules WHERE source_event = 'payment.received.vat.out_of_perimeter';
                """);
        }
    }
}
