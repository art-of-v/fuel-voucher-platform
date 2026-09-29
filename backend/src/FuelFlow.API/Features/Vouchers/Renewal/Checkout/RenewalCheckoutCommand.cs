namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>
/// A batch renewal/replacement checkout: the customer selects one term per voucher and pays for the
/// whole set with a single Monobank invoice. Mixed extend/replace across the batch is fine — the
/// branch is resolved per voucher, both at quote time (to gate stock) and again at fulfilment time.
/// </summary>
public sealed class RenewalCheckoutCommand
{
    /// <summary>Set by the controller from the authenticated caller's claims; never trusted from the body.</summary>
    public Guid? UserId { get; set; }

    public List<RenewalCheckoutItem> Items { get; set; } = new();
}

/// <summary>One voucher the customer wants to renew, with the tier code they chose for it.</summary>
public sealed class RenewalCheckoutItem
{
    public Guid VoucherId { get; set; }

    /// <summary>Stable tier code (e.g. <c>"2w"</c>, <c>"3m"</c>) — see <see cref="VoucherRenewalTerms.Code"/>.</summary>
    public string TermCode { get; set; } = string.Empty;
}
