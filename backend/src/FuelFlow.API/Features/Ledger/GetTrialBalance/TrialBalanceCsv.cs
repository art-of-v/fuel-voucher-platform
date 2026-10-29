using System.Globalization;
using System.Text;
using FuelFlow.Features.Ledger.SharedModels;

namespace FuelFlow.Features.Ledger.GetTrialBalance;

/// <summary>
/// Renders the close check as CSV, for the accountant's spreadsheet.
/// </summary>
/// <remarks>
/// Invariant numbers and a UTF-8 BOM, like the reconciliation act: Excel decides the encoding from the
/// BOM alone, and without it the account names arrive as mojibake.
/// </remarks>
public static class TrialBalanceCsv
{
    public static byte[] RenderBytes(TrialBalanceResponse report)
    {
        var body = Encoding.UTF8.GetBytes(Render(report));
        var preamble = Encoding.UTF8.GetPreamble();

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    public static string Render(TrialBalanceResponse report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Trial balance");
        sb.AppendLine(FormattableString.Invariant($"Period,{report.FromDate:yyyy-MM-dd},{report.ToDate:yyyy-MM-dd}"));
        sb.AppendLine("Balanced," + Yes(report.Totals.Balanced));
        sb.AppendLine("Bank matches the books," + Yes(report.Bank.Matches));
        sb.AppendLine("Unbooked captures," + report.UnbookedCaptures.Count.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Ready to close," + Yes(report.ReadyToClose));
        sb.AppendLine();

        sb.AppendLine("Posted accounts");
        AppendAccounts(sb, report.Accounts);
        sb.AppendLine();

        sb.AppendLine("Bank check");
        sb.AppendLine("Merchant," + report.Bank.Merchant);
        sb.AppendLine("Statement received UAH," + N(report.Bank.StatementReceivedUah));
        sb.AppendLine("Statement rows," + report.Bank.StatementRowCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("Ledger 311 debit UAH," + N(report.Bank.Ledger311DebitUah));
        sb.AppendLine("Ledger 311 credit UAH," + N(report.Bank.Ledger311CreditUah));
        sb.AppendLine("Ledger 311 closing UAH," + N(report.Bank.Ledger311ClosingUah));
        sb.AppendLine("Difference UAH," + N(report.Bank.DifferenceUah));
        sb.AppendLine("Matches," + Yes(report.Bank.Matches));
        sb.AppendLine();

        if (report.UnbookedCaptures.Count > 0)
        {
            sb.AppendLine("Received captures with no payment row");
            sb.AppendLine("InvoiceId,Amount,PaidAt,StatementRows,Rrn");
            foreach (var capture in report.UnbookedCaptures)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    Q(capture.InvoiceId),
                    N(capture.AmountUah),
                    Q(capture.PaidAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    capture.StatementRows.ToString(CultureInfo.InvariantCulture),
                    Q(capture.Rrn)
                }));
            }

            sb.AppendLine();
        }

        if (report.Drafts.Count > 0)
        {
            sb.AppendLine("Draft entries (not in the books)");
            AppendAccounts(sb, report.Drafts);
        }

        return sb.ToString();
    }

    private static void AppendAccounts(StringBuilder sb, IReadOnlyList<TrialBalanceAccount> accounts)
    {
        sb.AppendLine("Code,Account,Type,Normal side,Opening,Opening side,Debit,Credit,Closing,Closing side");

        foreach (var account in accounts)
        {
            sb.AppendLine(string.Join(',', new[]
            {
                Q(account.Code),
                Q(account.NameUkr),
                Q(account.AccountType.ToString()),
                Q(account.NormalBalance.ToString()),
                N(account.OpeningBalance),
                Q(account.OpeningSide?.ToString()),
                N(account.Debit),
                N(account.Credit),
                N(account.ClosingBalance),
                Q(account.ClosingSide?.ToString())
            }));
        }
    }

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string N(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Q(string? value)
        => value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
