using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Gives the collected VAT somewhere to go: 641 «Розрахунки за податками».
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>payment.received.vat</c> credits 643 for the VAT inside every payment, and nothing ever
    /// debited it: the chart had no settlement account, so the balance meant "VAT charged since
    /// go-live" rather than "VAT owed to the budget" (planning #787, found by the October close #780).
    /// </para>
    /// <para>
    /// <b>Settlement is an operator entry, not an event.</b> Filing a VAT return is a human act on
    /// the accountant's calendar, and the amount is the return's - not something the platform can
    /// derive from a payment. The operator posts <c>Dr 643 / Cr 641</c> for the return's VAT when it
    /// is filed, and <c>Dr 641 / Cr 311</c> when it is paid; both go through
    /// <c>POST /api/admin/ledger/entries</c>, which is idempotent on the entry's reference. A mapping
    /// rule is deliberately not added: an event prints "caused by a row in the operational tables",
    /// and a filed return is caused by nothing in this database.
    /// </para>
    /// <para>
    /// <b>641 and not 644.</b> 644 is the input VAT credit, seeded and unused; 641 is the account
    /// that faces the budget, and it is the one a return is settled into under MinFin Order No. 291.
    /// </para>
    /// </remarks>
    public partial class TheTaxPayableHasAnAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Under the class-6 header, and only if it is not there: an accountant may already have
            // loaded their own chart, and a second 641 would make every resolution ambiguous.
            migrationBuilder.Sql("""
                INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, parent_id, is_postable, is_active)
                SELECT gen_random_uuid(), '641', 'Розрахунки за податками', 'Liability', 'Credit', h.id, true, true
                FROM chart_of_accounts h
                WHERE h.code = '64'
                  AND NOT EXISTS (SELECT 1 FROM chart_of_accounts a WHERE a.code = '641');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only when nothing references it: a posted line or a mapping on 641 means the account
            // is in use, and the foreign key would fail rather than let the row go quietly.
            migrationBuilder.Sql("""
                DELETE FROM chart_of_accounts a
                WHERE a.code = '641'
                  AND NOT EXISTS (SELECT 1 FROM ledger_mapping_rules r WHERE r.account_id = a.id)
                  AND NOT EXISTS (SELECT 1 FROM journal_lines l WHERE l.account_id = a.id);
                """);
        }
    }
}
