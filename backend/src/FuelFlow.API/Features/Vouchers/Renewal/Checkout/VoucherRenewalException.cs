namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>
/// A renewal checkout was rejected for a business reason the customer can act on: the feature is off,
/// a voucher is not theirs / not renewable / outside the trigger window, the chosen tier is not
/// offerable, or no replacement stock is available. Surfaces as HTTP 400 with a stable
/// <see cref="Code"/> plus a human message — distinct from <c>AccountInactiveException</c> (403) and
/// unexpected faults (500).
///
/// <para><see cref="Data"/> carries whatever the caller needs in order to *act* on the rejection, as
/// numbers, rather than only inside the English sentence. A below-cost refusal is useless without
/// the shortfall: the operator has to decide whether it is a rounding artefact or a real loss, and a
/// localised sentence with the figure baked into it gives the admin no way to show that figure in
/// its own words. The message stays for logs, and for any client that has not learnt the code yet.</para>
/// </summary>
public sealed class VoucherRenewalException : Exception
{
    public string Code { get; }

    /// <summary>Structured detail for this code — empty when the code needs none.</summary>
    public IReadOnlyDictionary<string, decimal> Data { get; }

    public VoucherRenewalException(
        string code,
        string message,
        IReadOnlyDictionary<string, decimal>? data = null)
        : base(message)
    {
        Code = code;
        Data = data ?? new Dictionary<string, decimal>();
    }
}
