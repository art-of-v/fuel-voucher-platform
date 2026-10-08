namespace FuelFlow.Features.Vouchers.History;

/// <summary>
/// One dated entry in a voucher's life as the customer experiences it: the original purchase, then
/// each paid renewal. A renewal that <em>replaced</em> the voucher (issuing a different physical
/// voucher) is still one continuous history here - the chain is walked across the swap so the
/// customer sees a single asset, not a graveyard of expired ids.
/// </summary>
public sealed record VoucherHistoryEventDto(
    VoucherHistoryEventType Type,
    DateTime Date,
    decimal Liters,
    decimal? Amount,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    string? TermCode,
    // True only for a renewal that REPLACED the voucher (a different physical voucher was issued and
    // the old one expired), so the UI can say "replaced" rather than mislabel it "extended". Always
    // false for a purchase and for an extend-in-place renewal.
    bool IsReplacement = false);

public enum VoucherHistoryEventType
{
    Purchase,
    Renewal
}
