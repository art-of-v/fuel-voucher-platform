using System.Text.Json.Serialization;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

/// <summary>
/// One row of a Monobank merchant statement - what the acquirer says happened in a period.
/// </summary>
/// <remarks>
/// Deliberately all-nullable except <see cref="StatementId"/>. The acquirer is the authority on
/// money, so this is read as "whatever they reported", not reshaped into what we expected. A field
/// that is suddenly absent from a response is information the reconciliation needs to see; a
/// required property would make the whole deserialisation fail instead and hide the rows that did
/// parse.
/// </remarks>
public sealed class MonobankStatementItem
{
    /// <summary>The acquirer's own id for the row. Stable across re-fetches of the same period,
    /// which is what makes an import idempotent.</summary>
    public string StatementId { get; set; } = null!;

    /// <summary>The invoice this row settles, when it settles one. Null for holds and other
    /// movements that never had an invoice.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Our reference, echoed back. Kept as evidence, NOT as a join key: bulk checkouts
    /// all send "FuelFlow Bundle" and every renewal sends "FuelFlow Voucher Renewal", so
    /// thousands of distinct invoices share these strings. <see cref="InvoiceId"/> is the only
    /// reliable key.</summary>
    public string? Reference { get; set; }

    /// <summary>Amount in kopecks, as the acquirer sends it. Never converted to decimal here.</summary>
    public long? Amount { get; set; }

    /// <summary>Currency code (980 = UAH).</summary>
    public int? Ccy { get; set; }

    /// <summary>Acquirer-side status. Not the same vocabulary as <c>MonobankStatus</c> on our order,
    /// and deliberately not mapped onto it - reconciliation compares the two.</summary>
    public string? Status { get; set; }

    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Refund/hold cancellation reference from <c>invoice/cancel</c>.</summary>
    public string? ExtRef { get; set; }

    /// <summary>Last four digits of the paying card, when the acquirer reports one.</summary>
    public string? MaskedCard { get; set; }

    public string? Rrn { get; set; }
    public string? ApprovalCode { get; set; }

    /// <summary>
    /// This row exactly as the acquirer sent it, captured during deserialisation.
    /// </summary>
    /// <remarks>
    /// Set by the client, never by the wire format - hence <see cref="JsonIgnoreAttribute"/>. It
    /// exists so the row can be stored and re-read later without re-fetching it from the
    /// acquirer, which is not possible for a real statement we have already seen once.
    /// </remarks>
    [JsonIgnore]
    public string? RawJson { get; set; }
}