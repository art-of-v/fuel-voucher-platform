namespace FuelFlow.Features.Vouchers.Renewal.Quote;

/// <summary>
/// Result of a renewal quote: the feature gate the client needs plus a per-voucher preview. When
/// <see cref="Enabled"/> is false the feature is off and <see cref="Vouchers"/> is empty — the client
/// should hide the whole renewal affordance.
/// </summary>
public sealed class RenewalQuoteResponse
{
    public bool Enabled { get; set; }

    /// <summary>Trigger window: a voucher is renewable only when its expiry ≤ today + this many days.</summary>
    public int ThresholdDays { get; set; }

    public List<RenewalVoucherQuote> Vouchers { get; set; } = new();
}

/// <summary>One voucher's renewal preview: its metadata, whether it can be renewed, and the tier ladder.</summary>
public sealed class RenewalVoucherQuote
{
    public Guid VoucherId { get; set; }

    /// <summary>True when this voucher may be renewed right now (owned, renewable status, in window).</summary>
    public bool Eligible { get; set; }

    /// <summary>
    /// Why the voucher can't be renewed, when <see cref="Eligible"/> is false: a stable code the client
    /// maps to copy — <c>not_your_voucher</c>, <c>not_renewable</c>. Null when eligible.
    /// </summary>
    public string? IneligibleReason { get; set; }

    /// <summary><c>extend</c> (still valid) or <c>replace</c> (lapsed); null when not eligible.</summary>
    public string? Branch { get; set; }

    public string Provider { get; set; } = string.Empty;
    public string FuelTypeId { get; set; } = string.Empty;
    public string FuelName { get; set; } = string.Empty;
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }

    /// <summary>The eight tiers with per-voucher price and availability; empty when not eligible.</summary>
    public List<RenewalTermQuote> Terms { get; set; } = new();
}

/// <summary>One tier for one voucher: the price the customer would pay and whether it can be bought.</summary>
public sealed class RenewalTermQuote
{
    /// <summary>Stable tier code (<c>1w</c>, <c>2w</c>, <c>1m</c>…<c>6m</c>) — sent back verbatim on checkout.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Whole-UAH price for renewing THIS voucher for this term (litres × rate, rounded).</summary>
    public int PriceUah { get; set; }

    /// <summary>True when the customer can buy this tier for this voucher right now.</summary>
    public bool Available { get; set; }

    /// <summary>
    /// Why a tier is unavailable, when <see cref="Available"/> is false: <c>not_offerable</c> (manager
    /// disabled it or set no price) or <c>no_stock</c> (replace branch, nothing in stock reaches the
    /// term). Null when available.
    /// </summary>
    public string? UnavailableReason { get; set; }
}
