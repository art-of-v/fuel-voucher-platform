namespace FuelFlow.Features.Vouchers.Renewal.Quote;

/// <summary>
/// A read-only price/eligibility preview for a batch of the caller's own vouchers. The mobile client
/// sends the vouchers the customer is about to renew and gets back, per voucher, whether it is
/// renewable, which branch it takes, and the eight tiers with their price and availability — so the
/// term picker can disable ("тимчасово недоступно") a tier BEFORE any payment. <see cref="UserId"/>
/// is set from the auth claims server-side; it is never trusted from the request body.
/// </summary>
public sealed class RenewalQuoteCommand
{
    public Guid? UserId { get; set; }

    public List<Guid> VoucherIds { get; set; } = new();
}
