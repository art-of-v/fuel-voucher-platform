namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>
/// A renewal checkout was rejected for a business reason the customer can act on: the feature is off,
/// a voucher is not theirs / not renewable / outside the trigger window, the chosen tier is not
/// offerable, or no replacement stock is available. Surfaces as HTTP 400 with a stable
/// <see cref="Code"/> plus a human message — distinct from <c>AccountInactiveException</c> (403) and
/// unexpected faults (500).
/// </summary>
public sealed class VoucherRenewalException : Exception
{
    public string Code { get; }

    public VoucherRenewalException(string code, string message) : base(message)
    {
        Code = code;
    }
}
