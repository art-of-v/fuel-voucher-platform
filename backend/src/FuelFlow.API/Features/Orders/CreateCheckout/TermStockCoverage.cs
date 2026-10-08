using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Answers the one question the buy-flow term ladder must settle before it offers a term: is there stock
/// whose paper term actually reaches that far? A term the station cannot honour must never be on the
/// ladder, because the customer would pay the long-term price and receive the shorter validity the stock
/// allows (the clamp in <c>FulfillmentService.TryAssignVoucherAsync</c>).
/// </summary>
/// <remarks>
    /// Deliberately the same rule the renewal-replace path applies — the renew quote measures its best stock
    /// expiry against <see cref="VoucherRenewalEligibility.PromisedExpirationForReplace"/> — so both ladders
    /// answer identically instead of drifting apart. A buy has no leftover term to preserve, so passing
    /// <paramref name="today"/> as the prior date collapses the promise to <c>today + term</c>, which is the
    /// whole point of that helper. The fulfilment clamp stays as the last line of defence for stock that
    /// moves between quote and payment.
/// </remarks>
public static class TermStockCoverage
{
    /// <summary>
    /// The latest paper expiry among <see cref="VoucherStatus.Available"/> vouchers matching the provider,
    /// fuel type and nominal, or null when nothing is in stock. Provider is matched case-insensitively:
    /// order lines and the catalog carry <c>okko</c> while imported stock carries <c>OKKO</c>, and the
    /// fulfilment matcher compares the same way.
    /// </summary>
    public static async Task<DateOnly?> BestStockExpirationAsync(
        ApplicationDbContext context,
        string stationId,
        string fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        var provider = stationId.ToLower();

        return await context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Provider.ToLower() == provider
                     && v.FuelTypeId == fuelTypeId
                     && v.Liters == liters)
            .OrderByDescending(v => v.ProviderExpirationDate)
            .Select(v => (DateOnly?)v.ProviderExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the best in-stock paper term can cover <paramref name="term"/> — that is, the stock stays
    /// valid at least until <c>today + term</c> (a purchase keeps no leftover days, so there is nothing to
    /// add the term to). No stock at all covers nothing, so the whole ladder drops.
    /// </summary>
    public static bool CanHonour(DateOnly? bestStockExpiration, DateOnly today, VoucherRenewalTerm term)
        => bestStockExpiration.HasValue
           && bestStockExpiration.Value >= VoucherRenewalEligibility.PromisedExpirationForReplace(today, today, term);
}
