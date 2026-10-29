using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the VAT share of an advance its own account, stops carving it out twice, and maps the
    /// receipt that has no order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three corrections in one data migration, all from the October close (planning #780).
    /// </para>
    /// <para>
    /// <b>703 was mislabelled and is not a deduction account.</b> Under MinFin Order No. 291 of
    /// 30.11.1999, 703 is <i>Дохід від реалізації робіт і послуг</i> - service revenue - and
    /// <i>Вирахування з доходу</i> is 704. The seed gave 703 the deduction's name and posted both
    /// legs of the VAT pass-through to it (Dr at receipt, Cr at redemption), which read as a revenue
    /// account carrying a debit balance. 703 keeps its code and its history - posted lines are
    /// immutable - and is renamed to what the plan calls it; 704 is added and takes the receipt leg.
    /// </para>
    /// <para>
    /// <b>The VAT is carved out once, when the money arrives.</b> <c>voucher.redeemed.vat</c> (Dr 643
    /// / Cr 703) carved the same VAT out a second time at redemption and released the liability that
    /// the receipt had raised, which left an earned advance's VAT on 681 and no VAT liability
    /// anywhere. The event is retired: revenue is released gross and the receipt's pair stands. The
    /// drafts it left in October are folded into the revenue drafts - the drafts are not in the books,
    /// so they are corrected here rather than reversed in the open period like a posted entry.
    /// </para>
    /// <para>
    /// <b>The lines already posted to 703 are left where they are.</b> A posted entry is what an
    /// auditor reads and this migration does not rewrite one. The close document records the
    /// reclassification (Dr 704 / Cr 703 for the 2 298.69 of October 2026) as an operator step in the
    /// open period, which is the only form a correction may take.
    /// </para>
    /// <para>
    /// <b>payment.received.unapplied.</b> Money the acquirer took with no order of ours used to have
    /// no path into the books at all, which is how the Live account sat 2.00 UAH below the statement.
    /// The event is Dr 311 / Cr 681 - bank against advances received - because the money is real and
    /// its purpose is not yet known. Revenue and a VAT split would assert a sale that never happened.
    /// </para>
    /// </remarks>
    public partial class TheVatShareGetsItsOwnAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 704 under the class-7 header, only if it is not there: an accountant may already have
            // loaded it from their own chart, and a second row with the same code would make every
            // mapping resolution ambiguous.
            migrationBuilder.Sql("""
                INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, parent_id, is_postable, is_active)
                SELECT gen_random_uuid(), '704', 'Вирахування з доходу', 'Revenue', 'Debit', h.id, true, true
                FROM chart_of_accounts h
                WHERE h.code = '70'
                  AND NOT EXISTS (SELECT 1 FROM chart_of_accounts a WHERE a.code = '704');
                """);

            // 703 keeps its code and its normal balance becomes what a revenue account's is. The
            // normal balance is informational - a posting leg's side comes from the mapping rule -
            // but a revenue account declaring a debit balance is the same mislabel in a second column.
            migrationBuilder.Sql("""
                UPDATE chart_of_accounts
                SET name_ukr = 'Дохід від реалізації робіт і послуг', normal_balance = 'Credit'
                WHERE code = '703' AND name_ukr = 'Вирахування з доходу';
                """);

            // The receipt leg moves to the deduction account. The redemption leg is not moved - it is
            // retired below, because the same VAT must not be carved out of the sale as well.
            migrationBuilder.Sql("""
                UPDATE ledger_mapping_rules
                SET account_id = (SELECT id FROM chart_of_accounts WHERE code = '704')
                WHERE source_event = 'payment.received.vat'
                  AND account_id = (SELECT id FROM chart_of_accounts WHERE code = '703');
                """);

            // Fold the net revenue drafts up to the gross before their other half disappears: each
            // redemption's VAT draft says exactly how much was carved out, so the pair reconstructs
            // the gross without re-deriving it from a price that may have moved since.
            migrationBuilder.Sql("""
                WITH vat_drafts AS (
                    SELECT e.source_id, SUM(l.credit_uah) AS amount
                    FROM journal_entries e
                    JOIN journal_lines l ON l.entry_id = e.id
                    WHERE e.source_event = 'voucher.redeemed.vat' AND e.status = 'Draft'
                    GROUP BY e.source_id
                )
                UPDATE journal_lines l
                SET debit_uah  = l.debit_uah  + CASE WHEN l.debit_uah  > 0 THEN v.amount ELSE 0 END,
                    credit_uah = l.credit_uah + CASE WHEN l.credit_uah > 0 THEN v.amount ELSE 0 END
                FROM journal_entries e
                JOIN vat_drafts v ON v.source_id = e.source_id
                WHERE l.entry_id = e.id
                  AND e.source_event = 'voucher.redeemed.revenue'
                  AND e.status = 'Draft';
                """);

            // Lines first: journal_lines' foreign key is RESTRICT.
            migrationBuilder.Sql("""
                DELETE FROM journal_lines l
                USING journal_entries e
                WHERE l.entry_id = e.id
                  AND e.source_event = 'voucher.redeemed.vat'
                  AND e.status = 'Draft';
                """);

            migrationBuilder.Sql("""
                DELETE FROM journal_entries
                WHERE source_event = 'voucher.redeemed.vat' AND status = 'Draft';
                """);

            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules WHERE source_event = 'voucher.redeemed.vat';
                """);

            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), 'Payment'::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    ('payment.received.unapplied', '311', 'Debit'),
                    ('payment.received.unapplied', '681', 'Credit')
                ) AS m(source_event, code, side)
                JOIN chart_of_accounts a ON a.code = m.code
                WHERE NOT EXISTS (
                    SELECT 1 FROM ledger_mapping_rules r WHERE r.source_event = m.source_event);
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The draft conversion is deliberately not undone. A draft the previous build would have
        /// composed as net revenue plus a VAT pair cannot be reconstructed from what remains without
        /// re-deriving the split from today's prices, and a rollback that invents bookkeeping is worse
        /// than one that leaves a draft the close check reports.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules WHERE source_event = 'payment.received.unapplied';
                """);

            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), 'Fulfillment'::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    ('voucher.redeemed.vat', '643', 'Debit'),
                    ('voucher.redeemed.vat', '703', 'Credit')
                ) AS m(source_event, code, side)
                JOIN chart_of_accounts a ON a.code = m.code
                WHERE NOT EXISTS (
                    SELECT 1 FROM ledger_mapping_rules r WHERE r.source_event = m.source_event);
                """);

            migrationBuilder.Sql("""
                UPDATE ledger_mapping_rules
                SET account_id = (SELECT id FROM chart_of_accounts WHERE code = '703')
                WHERE source_event = 'payment.received.vat'
                  AND account_id = (SELECT id FROM chart_of_accounts WHERE code = '704');
                """);

            migrationBuilder.Sql("""
                UPDATE chart_of_accounts
                SET name_ukr = 'Вирахування з доходу', normal_balance = 'Debit'
                WHERE code = '703' AND name_ukr = 'Дохід від реалізації робіт і послуг';
                """);

            // Only when nothing references it: an accountant may have posted to 704 since, and
            // deleting an account with lines would violate the foreign key rather than fail cleanly.
            migrationBuilder.Sql("""
                DELETE FROM chart_of_accounts a
                WHERE a.code = '704'
                  AND NOT EXISTS (SELECT 1 FROM ledger_mapping_rules r WHERE r.account_id = a.id)
                  AND NOT EXISTS (SELECT 1 FROM journal_lines l WHERE l.account_id = a.id);
                """);
        }
    }
}
