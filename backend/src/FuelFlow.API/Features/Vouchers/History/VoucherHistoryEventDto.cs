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
    string? TermCode);

public enum VoucherHistoryEventType
{
    Purchase,
    Renewal
}
