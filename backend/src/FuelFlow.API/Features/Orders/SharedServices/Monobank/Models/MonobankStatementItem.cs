using System.Globalization;
using System.Text.Json.Serialization;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

/// <summary>
/// One row of a Monobank merchant statement - what the acquirer says happened in a period.
/// </summary>
/// <remarks>
/// <para>
/// Field-for-field against the acquirer's documented statement payload
/// (<c>GET /api/merchant/statement</c>, monobank.com.ua → Еквайринг → Виписка): <c>invoiceId</c>,
/// <c>status</c>, <c>maskedPan</c>, <c>date</c>, <c>amount</c>, <c>ccy</c>, <c>reference</c>,
/// <c>destination</c>, <c>approvalCode</c>, <c>rrn</c>. Property names match the wire on purpose -
/// a name that has to be translated is a name the next reader cannot check against the docs.
/// </para>
/// <para>
/// Deliberately all-nullable except <see cref="StatementId"/>. The acquirer is the authority on
/// money, so this is read as "whatever they reported", not reshaped into what we expected. A field
/// that is suddenly absent from a response is information the reconciliation needs to see; a
/// required property would make the whole deserialisation fail instead and hide the rows that did
/// parse.
/// </para>
/// <para>
/// Two things the payload has and this does not model, on purpose: <c>profitAmount</c> (the amount
/// after settlement) and <c>cancelList</c> (the reversals Monobank nests under the payment they
/// reverse). Both are preserved verbatim in <see cref="RawJson"/>. Reading a reversal as a row of
/// its own needs a link back to the invoice it cancels, and the documented <c>cancelList</c> entry
/// carries only amount, currency, date, masked pan, approval code and rrn - filing those as
/// standalone rows would put money out in an accountant's act with nothing to point at, which is
/// worse than reporting the payment and leaving the reversal to <see cref="RawJson"/>.
/// </para>
/// </remarks>
public sealed class MonobankStatementItem
{
    /// <summary>
    /// The key this row is filed under. NOT the acquirer's: its statement carries no per-row id, so
    /// this is composed from what it does send - see <see cref="ComposeStatementId"/>. Set by the
    /// client after parsing, never by the wire format.
    /// </summary>
    [JsonIgnore]
    public string StatementId { get; set; } = null!;

    /// <summary>The invoice this row settles, when it settles one. Null for holds and other
    /// movements that never had an invoice. 1:1 with an order and the only reliable cross-match.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Acquirer-side status, in the acquirer's vocabulary (<c>hold</c>, <c>processing</c>,
    /// <c>success</c>, <c>failure</c>). Not the same as <c>MonobankStatus</c> on our order, and
    /// deliberately not mapped onto it - reconciliation compares the two.</summary>
    public string? Status { get; set; }

    /// <summary>Masked card number, e.g. <c>444403*******1902</c>.</summary>
    public string? MaskedPan { get; set; }

    /// <summary>Financial operation datetime (RFC-3339). The period a reconciliation filters on.</summary>
    public DateTime? Date { get; set; }

    /// <summary>Amount in kopecks, as the acquirer sends it. Never converted to decimal here.</summary>
    public long? Amount { get; set; }

    /// <summary>Currency code (980 = UAH).</summary>
    public int? Ccy { get; set; }

    /// <summary>Our merchant reference, echoed back. Evidence, NOT a join key: bulk checkouts
    /// all send "FuelFlow Bundle" and every renewal sends "FuelFlow Voucher Renewal", so
    /// thousands of distinct invoices share these strings. <see cref="InvoiceId"/> is the only
    /// reliable key.</summary>
    public string? Reference { get; set; }

    /// <summary>The payment purpose as it appears on the receipt - "Паливо", or whatever the
    /// checkout asked for. This, not <see cref="Reference"/>, is the human-readable string.</summary>
    public string? Destination { get; set; }

    /// <summary>Transaction id in the payment system.</summary>
    public string? Rrn { get; set; }

    /// <summary>Transaction authorization code.</summary>
    public string? ApprovalCode { get; set; }

    /// <summary>
    /// The key to file this row under, or null when the row carries nothing that could identify it.
    /// </summary>
    /// <remarks>
    /// The statement has no row id of its own, and re-importing a rolling window has to be
    /// idempotent or the table grows a duplicate set every hour and the act double-counts. What the
    /// acquirer does send is enough to name a payment uniquely: the invoice it settles, or failing
    /// that the payment-system identifiers, plus when it happened, how much, in what currency.
    /// Readable rather than hashed, because it is printed in the act an accountant signs.
    ///
    /// Null rather than a fallback: a row with no identity at all cannot be told apart from a second
    /// such row, and filing both under one key would let the second overwrite the first.
    /// </remarks>
    public string? ComposeStatementId()
    {
        var identity = InvoiceId;
        if (string.IsNullOrWhiteSpace(identity))
        {
            identity = Rrn;
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            identity = MaskedPan;
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }

        return FormattableString.Invariant(
            $"{identity}@{Date?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}/{Amount}/{Ccy}");
    }

    /// <summary>
    /// This row exactly as the acquirer sent it, captured during deserialisation.
    /// </summary>
    /// <remarks>
    /// Set by the client, never by the wire format - hence <see cref="JsonIgnoreAttribute"/>. It
    /// exists so the row can be stored and re-read later without re-fetching it from the
    /// acquirer, which is not possible for a real statement we have already seen once, and so the
    /// fields this class does not model (profitAmount, cancelList) are not lost for good.
    /// </remarks>
    [JsonIgnore]
    public string? RawJson { get; set; }
}