using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Seeds the chart of accounts and the event mapping, so the journal can carry money before the
    /// accountant exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These accounts are taken from the law, not invented.</b> Every code and name below is from
    /// the Plan of Accounts approved by MinFin Order No. 291 of 30.11.1999 and its Instruction for
    /// application, as amended. The class structure is the plan's own:
    /// </para>
    /// <list type="bullet">
    /// <item><description>class 1 - non-current assets (10-19)</description></item>
    /// <item><description>class 2 - inventories (20-28)</description></item>
    /// <item><description>class 3 - cash, settlements and other assets (30-39)</description></item>
    /// <item><description>class 4 - own capital and provisions (40-49)</description></item>
    /// <item><description>class 5 - long-term liabilities (50-55)</description></item>
    /// <item><description>class 6 - current liabilities (60-69)</description></item>
    /// <item><description>class 7 - income (70-79)</description></item>
    /// <item><description>class 8 - expenses by element (80-84)</description></item>
    /// <item><description>class 9 - operating expenses (90-98)</description></item>
    /// </list>
    /// <para>
    /// The reason this says so loudly: <c>TARGET_SCHEMA.md</c> previously carried a table of
    /// "representative" accounts numbered 1010 / 2010 / 2200 / 3100, as if the classes ran 1, 2, 3 in
    /// sequence. They do not. Under the real plan 1010 is <i>fixed assets</i>, inventory is account
    /// 28, VAT lives in class 64 and capital in class 40. Those numbers looked plausible, which is
    /// what made them dangerous: they would have produced a book that balanced and was wrong.
    /// </para>
    /// <para>
    /// <b>Only the accounts this business uses are seeded.</b> A chart of all six hundred accounts is
    /// not a plan of accounts, it is a printout, and it puts a hundred irrelevant postable accounts in
    /// front of whoever loads the accountant's own chart later. The rest is theirs to add.
    /// </para>
    /// <para>
    /// <b>Hierarchy.</b> The synthetic accounts (28, 31, 36, ...) are seeded as headers - present so
    /// the hierarchy reads correctly and a bookkeeper can see what a subaccount belongs to, but not
    /// postable, because a header is a grouping rather than a place to post. Only subaccounts take
    /// postings, and <c>JournalPostingService</c> refuses a header.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class SeedUkrainianChartOfAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // parent_id points back at this same table, so the headers go in first and the
            // subaccounts - which reference those headers - after.
            migrationBuilder.Sql("""
                INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, parent_id, is_postable, is_active)
                VALUES
                    (gen_random_uuid(), '28',  'Товари',                                       'Asset',     'Debit',  NULL, false, true),
                    (gen_random_uuid(), '31',  'Рахунки в банках',                              'Asset',     'Debit',  NULL, false, true),
                    (gen_random_uuid(), '36',  'Розрахунки з покупцями та замовниками',         'Asset',     'Debit',  NULL, false, true),
                    (gen_random_uuid(), '37',  'Розрахунки з різними дебіторами',                'Asset',     'Debit',  NULL, false, true),
                    (gen_random_uuid(), '40',  'Зареєстрований (пайовий) капітал',               'Equity',    'Credit', NULL, false, true),
                    (gen_random_uuid(), '44',  'Нерозподілені прибутки (непокриті збитки)',     'Equity',    'Credit', NULL, false, true),
                    (gen_random_uuid(), '63',  'Розрахунки з постачальниками та підрядниками', 'Liability', 'Credit', NULL, false, true),
                    (gen_random_uuid(), '64',  'Рахунки за податками і платежами',              'Liability', 'Credit', NULL, false, true),
                    (gen_random_uuid(), '68',  'Рахунки за іншими операціями',                 'Liability', 'Credit', NULL, false, true),
                    (gen_random_uuid(), '70',  'Доходи від реалізації',                         'Revenue',   'Credit', NULL, false, true),
                    (gen_random_uuid(), '80',  'Матеріальні витрати',                           'Expense',   'Debit',  NULL, false, true);
                """);

            migrationBuilder.Sql("""
                INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, parent_id, is_postable, is_active)
                SELECT gen_random_uuid(), s.code, s.name_ukr, s.account_type, s.normal_balance, h.id, true, true
                FROM (VALUES
                    ('281', 'Товари на складі',                         'Asset',     'Debit'),
                    ('311', 'Поточні рахунки в національній валюті',     'Asset',     'Debit'),
                    ('361', 'Розрахунки з вітчизняними покупцями',       'Asset',     'Debit'),
                    ('371', 'Розрахунки за виданими авансами',            'Asset',     'Debit'),
                    ('401', 'Статутний капітал',                         'Equity',    'Credit'),
                    ('441', 'Прибуток нерозподілений',                   'Equity',    'Credit'),
                    ('631', 'Розрахунки з вітчизняними постачальниками',  'Liability', 'Credit'),
                    ('643', 'Податкові зобов''язання',                   'Liability', 'Credit'),
                    ('644', 'Податковий кредит',                         'Vat',       'Debit'),
                    ('681', 'Рахунки за авансами одержаними',            'Liability', 'Credit'),
                    ('702', 'Дохід від реалізації товарів',               'Revenue',   'Credit'),
                    -- Вирахування з доходу is the VAT contra-account: debited when VAT is carved out
                    -- of an advance, credited when it is carved out of a sale. That it takes both
                    -- sides is why a posting leg's side comes from the mapping rule and never from
                    -- the account's normal balance.
                    ('703', 'Вирахування з доходу',                      'Revenue',   'Debit'),
                    ('808', 'Витрати товарів',                            'Expense',   'Debit')
                ) AS s(code, name_ukr, account_type, normal_balance)
                JOIN chart_of_accounts h ON substr(s.code, 1, 2) = h.code;
                """);

            // The mapping. One account per side per event, which is what makes an entry a balanced
            // pair rather than a preference. Each row names an account by code and is resolved to an
            // id here, so the mapping reads without a join.
            migrationBuilder.Sql("""
                INSERT INTO ledger_mapping_rules (id, source_type, source_event, account_id, side)
                SELECT gen_random_uuid(), m.source_type::varchar, m.source_event, a.id, m.side
                FROM (VALUES
                    -- A customer pays for vouchers in advance. The money is not revenue yet - it is
                    -- an obligation, because the fuel has not been drawn. Hence 681, not 702.
                    ('Payment',     'payment.received',            '311', 'Debit'),
                    ('Payment',     'payment.received',            '681', 'Credit'),
                    -- VAT carved out of that advance. Dr 703 / Cr 643.
                    ('Payment',     'payment.received.vat',        '703', 'Debit'),
                    ('Payment',     'payment.received.vat',        '643', 'Credit'),
                    -- Repayment of an unspent advance.
                    ('Payment',     'payment.refunded',            '681', 'Debit'),
                    ('Payment',     'payment.refunded',            '311', 'Credit'),
                    -- Buying voucher stock: 808 is the element-based cost account, which is the
                    -- accounting-policy choice the plan itself defers to class 8.
                    ('Cost',        'stock.purchased',             '808', 'Debit'),
                    ('Cost',        'stock.purchased',             '281', 'Credit'),
                    ('Cost',        'stock.purchased.payable',     '281', 'Debit'),
                    ('Cost',        'stock.purchased.payable',     '631', 'Credit'),
                    -- Redemption: the advance is earned, so it leaves 681 and becomes revenue.
                    ('Fulfillment', 'voucher.redeemed.revenue',    '681', 'Debit'),
                    ('Fulfillment', 'voucher.redeemed.revenue',    '702', 'Credit'),
                    ('Fulfillment', 'voucher.redeemed.vat',        '643', 'Debit'),
                    ('Fulfillment', 'voucher.redeemed.vat',        '703', 'Credit'),
                    ('Fulfillment', 'voucher.redeemed.cogs',       '808', 'Debit'),
                    ('Fulfillment', 'voucher.redeemed.cogs',       '281', 'Credit')
                ) AS m(source_type, source_event, code, side)
                JOIN chart_of_accounts a ON a.code = m.code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // By event, not by account: an accountant who has since re-pointed a rule would leave
            // rows behind if this went by account, and these are the events this migration introduced.
            migrationBuilder.Sql("""
                DELETE FROM ledger_mapping_rules
                WHERE source_event IN (
                    'payment.received', 'payment.received.vat', 'payment.refunded',
                    'stock.purchased', 'stock.purchased.payable',
                    'voucher.redeemed.revenue', 'voucher.redeemed.vat', 'voucher.redeemed.cogs');
                """);

            migrationBuilder.Sql("""
                DELETE FROM chart_of_accounts
                WHERE code IN ('281','311','361','371','401','441','631','643','644','681','702','703','808',
                               '28','31','36','37','40','44','63','64','68','70','80');
                """);
        }
    }
}