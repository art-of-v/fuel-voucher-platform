using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Answers the one question the buy-flow term ladder must settle before it offers a term: is there stock
/// whose paper term actually reaches that far? A term the Brand cannot honour must never be on the
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
    /// The latest paper expiry among <see cref="VoucherStatus.Available"/> vouchers matching the brand,
    /// fuel type and nominal, or null when nothing is in stock.
    /// </summary>
    /// <param name="brandSlug">
    /// The network's slug, not its id. Vouchers store the brand <em>name</em> ("OKKO") and the two
    /// agree only because a slug is the lower-cased name; see <see cref="StockKey"/> for that
    /// assumption written down. The id stopped being usable here when planning D-21 gave it a uuid.
    /// </param>
    public static async Task<DateOnly?> BestStockExpirationAsync(
        ApplicationDbContext context,
        string brandSlug,
        Guid fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        var brand = brandSlug.ToLower();

        return await context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Brand.ToLower() == brand
                     && v.FuelTypeId == fuelTypeId
                     && v.Liters == liters)
            .OrderByDescending(v => v.SupplierExpirationDate)
            .Select(v => (DateOnly?)v.SupplierExpirationDate)
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
