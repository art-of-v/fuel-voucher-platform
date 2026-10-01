using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Vouchers.Renewal.Operator;

/// <summary>
/// One row per operator-initiated renewal of a CUSTOMER's voucher recorded in the admin app
/// (follow-up to planning #104, which covered only our own stock). When a customer renews their
/// voucher off-platform — paying the operator in cash / by transfer, or receiving a free
/// compensation — staff reflect it here: no Monobank, no in-app charge, no device signature.
///
/// The operation mirrors the mobile self-service renewal's two branches (#80): a still-valid voucher
/// is <b>extended in place</b> (same voucher, new expiry = old + term, <see cref="ReplacementVoucherId"/>
/// null); a lapsed voucher is <b>replaced</b> from stock (a fresh <c>Available</c> voucher is assigned
/// to the same customer, the old one expires, <see cref="ReplacementVoucherId"/> points at the new one).
/// The доплата the customer paid is recorded here for audit only — unlike the stock exchange it does
/// NOT touch blended stock cost (it is the customer's money, not a change to our cost of goods).
/// </summary>
public sealed class OperatorVoucherRenewal
{
    public Guid Id { get; set; }

    /// <summary>The customer's voucher that was renewed (extended) or retired (replace). FK → fuel_vouchers, Restrict.</summary>
    public Guid VoucherId { get; set; }

    /// <summary>
    /// The fresh stock voucher handed to the customer on the replace branch, or null on the extend
    /// branch (the same voucher was kept). Bare column — no FK, mirroring
    /// <c>VoucherRenewalItem.FulfilledVoucherId</c>: it is a nullable audit link, the <see cref="VoucherId"/>
    /// FK already pins the row to fuel_vouchers.
    /// </summary>
    public Guid? ReplacementVoucherId { get; set; }

    /// <summary>The customer who owns the voucher at the time of renewal.</summary>
    public Guid CustomerUserId { get; set; }

    /// <summary>Which branch ran: <c>extend</c> or <c>replace</c>.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>The chosen term as a stable <see cref="VoucherRenewalTerm"/> code (1w/2w/1m…6m).</summary>
    public string TermCode { get; set; } = string.Empty;

    /// <summary>Expiry of the customer's voucher before the renewal.</summary>
    public DateOnly OldExpiration { get; set; }

    /// <summary>Expiry the customer ends up with (extended voucher's new expiry, or the stock voucher's expiry).</summary>
    public DateOnly NewExpiration { get; set; }

    /// <summary>The доплата the customer paid off-platform; 0 = free compensation. Audit only — never reprices stock.</summary>
    public decimal SurchargeUah { get; set; }

    /// <summary>Optional provider накладна / invoice number.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Optional provider накладна / invoice date.</summary>
    public DateOnly? InvoiceDate { get; set; }

    /// <summary>Admin who recorded the renewal.</summary>
    public Guid ActingUserId { get; set; }

    /// <summary>Admin display name at the time, for audit.</summary>
    public string? ActingUserName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The customer's (renewed/retired) voucher.</summary>
    public FuelVoucher? Voucher { get; set; }
}
