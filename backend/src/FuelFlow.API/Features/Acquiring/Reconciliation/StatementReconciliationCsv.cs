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
                Q(A(row.AcquirerAmount)),
                Q(A(row.LocalAmount)),
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
                    Q(A(refund.Amount)),
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
                    Q(A(missing.Amount)),
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

    private static string Q(string? value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}