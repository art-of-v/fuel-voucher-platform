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
/// One thing the payload has and this does not model, on purpose: <c>profitAmount</c> (the amount
/// after settlement). It is preserved verbatim in <see cref="RawJson"/>. Nothing reads it, and
/// nothing would know what to do with it: the amount after settlement is the acquirer's own
/// arithmetic, and an act that quoted it beside ours would be quoting two different numbers for
/// the same money.
/// </para>
    /// <para>
    /// <b>The reversals are modelled, and not imported.</b> <c>cancelList</c> is the list Monobank
    /// nests under the payment it reverses. The shape is documented and the model is here so that it
    /// can be read, tested and reasoned about instead of hiding in the raw blob - what the act DOES
    /// with a reversal is a separate question, and it was decided on 2026-10-21 (planning #297): the
    /// act reconciles the refunds from our side, so the refunds we issued in the period stand beside
    /// the statement with a figure of their own, and the acquirer's statement stays the authority on
    /// what arrived. Filing a reversal as a row of its own would put money out in a document an
    /// accountant signs with nothing to point at, which is worse than reporting the payment and
    /// leaving the reversal to <see cref="RawJson"/>.
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

    private List<MonobankStatementCancellation>? _cancelList;

    /// <summary>
    /// The reversals Monobank nests under this payment. Empty for a payment nobody reversed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Modelled, and not imported. The shape is documented, so it can be read and tested instead of
    /// hiding in the raw blob; what the reconciliation act DOES with a reversal is a separate
    /// question, and it was decided on 2026-10-21 (planning #297) - the act reconciles the refunds
    /// from our side - and the two are separate: modelling a field changes nothing about what the
    /// act reports.
    /// </para>
    /// <para>
    /// <b>The link to the payment a reversal reverses is the nesting itself</b> - the parent row's
    /// invoice id - and not anything inside the entry. The entry carries no invoice id of its own,
    /// which is exactly why it can never be a match key on its own: a reversal of two identical
    /// payments on the same card is indistinguishable from the entry alone.
    /// </para>
    /// <para>
    /// Never null, whatever the wire says: a missing list and an explicit null both read as "no
    /// reversals", so a caller never has to ask which of the two it got.
    /// </para>
    /// </remarks>
    public List<MonobankStatementCancellation> CancelList
    {
        get => _cancelList ??= [];
        set => _cancelList = value;
    }

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
    /// fields this class does not model (profitAmount) are not lost for good.
    /// </remarks>
    [JsonIgnore]
    public string? RawJson { get; set; }
}

/// <summary>
/// One reversal, as the acquirer nests it under the payment it reverses.
/// </summary>
/// <remarks>
/// <para>
/// The documented entry carries no invoice id and nothing that names the payment on its own: amount,
/// currency, date, masked pan, approval code and rrn. The link to the payment it reverses is the
/// NESTING - the parent row - which is why the entry is held on the parent
/// (<see cref="MonobankStatementItem.CancelList"/>) and not on a row of its own.
/// </para>
/// <para>
/// Every field is nullable for the same reason as the rest of the wire model: the acquirer is the
/// authority on money, and a field that is suddenly absent from a response is information rather
/// than a reason to fail the whole row.
/// </para>
/// </remarks>
/// <param name="Amount">Kopecks, as the acquirer reports them. Never converted here.</param>
/// <param name="Ccy">Currency code (980 = UAH).</param>
/// <param name="Date">When the reversal happened.</param>
/// <param name="MaskedPan">The card the reversal was on.</param>
/// <param name="ApprovalCode">The transaction authorization code.</param>
/// <param name="Rrn">The transaction id in the payment system.</param>
public sealed record MonobankStatementCancellation(
    long? Amount,
    int? Ccy,
    DateTime? Date,
    string? MaskedPan,
    string? ApprovalCode,
    string? Rrn);