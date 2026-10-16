using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Vouchers.Renewal;

/// <summary>
/// One voucher line inside a paid renewal order. Records which source voucher the customer is
/// renewing and the term they bought (<see cref="TermCode"/>). A renewal order (created by the
/// checkout handler) owns one row per selected voucher; a batch = one order = one Monobank invoice
/// with several of these.
///
/// The two <c>Fulfilled…</c> fields are the post-payment done-marker: they stay null until the
/// fulfilment job has applied this line, then hold the voucher the customer ended up with
/// (the extended source voucher, or the fresh stock voucher on the replace branch) and when.
/// The marker is what makes fulfilment idempotent — a replaced source voucher's <c>Expired</c>
/// status is otherwise indistinguishable from a not-yet-processed lapsed voucher.
/// </summary>
public sealed class VoucherRenewalItem
{
    public Guid Id { get; set; }

    /// <summary>The renewal order this line belongs to (one order per batch).</summary>
    public Guid OrderId { get; set; }

    /// <summary>The customer's voucher being renewed/replaced.</summary>
    public Guid SourceVoucherId { get; set; }

    /// <summary>
    /// The order line this renewal charges, named explicitly at checkout.
    /// </summary>
    /// <remarks>
    /// A renewal order does carry line items — one per renewed voucher, mirroring the source's
    /// brand/fuel/nominal — and they exist so the shared refund maths keeps working. Until this
    /// column, the correspondence between a renewal item and its line was only positional, and the
    /// fulfilment path reads these rows by order id with no guaranteed ordering. Matching them
    /// afterwards on (brand, fuel, litres) is the same ambiguity the fulfilment reconciliation just
    /// had, moved one level up: a batch renewing two 10 L vouchers of the same fuel has two candidate
    /// lines and nothing to choose between them.
    /// <para>
    /// Set where both objects are in hand, which is the one moment the truth is available. Null for
    /// rows predating it; those renewals simply keep <c>Fulfillment.LineItemId</c> null too.
    /// </para>
    /// </remarks>
    public Guid? LineItemId { get; set; }

    /// <summary>The bought term as a stable <see cref="VoucherRenewalTerm.Code"/> (1w/2w/1m…6m).</summary>
    public string TermCode { get; set; } = string.Empty;

    /// <summary>
    /// Null until fulfilled. After fulfilment: the voucher the customer holds — the same
    /// (extended) source voucher on the extend branch, or the new stock voucher on the replace branch.
    /// </summary>
    public Guid? FulfilledVoucherId { get; set; }

    /// <summary>When this line was fulfilled (null until then).</summary>
    public DateTime? FulfilledAtUtc { get; set; }

    /// <summary>
    /// What the customer paid for this renewal line, in UAH, frozen at checkout (mirrors the
    /// order line's <c>LineTotal</c>). Kept here so the per-voucher history can show the amount
    /// of each event without re-deriving it from the batch order. Null for rows predating this
    /// column (no backfill).
    /// </summary>
    public decimal? AmountPaid { get; set; }

    /// <summary>
    /// The voucher's customer expiration immediately BEFORE this renewal was applied, captured at
    /// fulfilment. With <see cref="NewCustomerExpiration"/> it gives the exact "valid from -> to"
    /// range this event bought. Null until fulfilled and for rows predating this column.
    /// </summary>
    public DateOnly? PreviousCustomerExpiration { get; set; }

    /// <summary>
    /// The voucher's customer expiration immediately AFTER this renewal was applied, captured at
    /// fulfilment. See <see cref="PreviousCustomerExpiration"/>.
    /// </summary>
    public DateOnly? NewCustomerExpiration { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Order? Order { get; set; }
    public FuelVoucher? SourceVoucher { get; set; }

    public FuelVoucher? FulfilledVoucher { get; set; }
}

