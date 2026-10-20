using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
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
                     && v.Brand == brand
                     && v.FuelTypeId == fuelTypeId
                     && v.Liters == liters)
            .OrderByDescending(v => v.SupplierExpirationDate)
            .Select(v => (DateOnly?)v.SupplierExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The same answer for a whole cart, in one query: the latest paper expiry per
    /// (brand, fuel type, litres).
    /// </summary>
    /// <remarks>
    /// The single-line path asks the database once per cart line, so a bulk checkout with twenty
    /// lines asked it twenty times for the same question - and every one of those questions was
    /// the same GROUP BY the database can do for all of them at once.
    ///
    /// The narrowing is three IN-lists rather than one list of the keys, because a composite
    /// `Contains` cannot be translated - the same wall `PackageStockAvailability` documents. The
    /// groups that come back are the triples the vouchers really have, so a triple with no stock
    /// is simply absent, and absent means "no stock" - the same answer the single-line query gave.
    /// </remarks>
    public static async Task<Dictionary<StockKey, DateOnly?>> BestStockExpirationsAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<StockKey> wanted,
        CancellationToken cancellationToken = default)
    {
        if (wanted.Count == 0)
            return [];

        var brands = wanted.Select(k => k.Brand).Distinct().ToArray();
        var fuelTypeIds = wanted.Select(k => k.FuelTypeId).Distinct().ToArray();
        var literSizes = wanted.Select(k => k.Liters).Distinct().ToArray();

        var rows = await context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && brands.Contains(v.Brand)
                     && fuelTypeIds.Contains(v.FuelTypeId)
                     && literSizes.Contains(v.Liters))
            // The grouping is what makes this one row per triple instead of one row per voucher.
            .GroupBy(v => new { v.Brand, v.FuelTypeId, v.Liters })
            .Select(g => new
            {
                g.Key.Brand,
                g.Key.FuelTypeId,
                g.Key.Liters,
                Best = g.Max(v => v.SupplierExpirationDate)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            r => StockKey.For(r.Brand, r.FuelTypeId, r.Liters),
            r => (DateOnly?)r.Best);
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
