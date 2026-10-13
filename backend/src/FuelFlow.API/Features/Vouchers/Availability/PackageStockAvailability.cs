using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Availability;

/// <summary>
/// The composite a voucher is matched on, and therefore the composite a package's stock is counted by:
/// brand + fuel type + nominal. <see cref="Brand"/> is always lower-cased, because the catalog and
/// order lines carry <c>okko</c> while imported stock carries <c>OKKO</c> and every matcher compares
/// case-insensitively.
/// </summary>
public readonly record struct StockKey(string Brand, string FuelTypeId, decimal Liters)
{
    public static StockKey For(string brand, string fuelTypeId, decimal liters)
        => new(brand.ToLowerInvariant(), fuelTypeId, liters);
}

/// <summary>
/// One definition of "this package can actually be sold", shared by the store front that advertises it
/// and the checkout that charges for it.
/// </summary>
/// <remarks>
/// This mirrors <c>FulfillmentService.FindMatchingVoucherAsync</c> key for key — <see cref="VoucherStatus.Available"/>
/// only (imported stock is not sellable until a manager activates it, which in turn requires the batch cost),
/// the caller's own test-data pool, and the supplier term. Deliberately the same rule on both sides of the
/// counter: a package the shop advertises as available and then refuses to sell is the bug this class exists
/// to prevent, and a package it hides while fulfilment would happily hand the voucher over wastes a sale.
///
/// The predicate is an offer, not a reservation. Nothing is held here, so two customers can still race for
/// the last voucher — fulfilment remains the authority, and an order that loses the race simply sits
/// <c>PartiallyFulfilled</c> until the operator restocks. What this removes is the common, avoidable failure:
/// paying for fuel the shop already said it did not have.
/// </remarks>
public sealed class PackageStockAvailability
{
    private readonly ApplicationDbContext _context;

    /// <summary>
    /// Whether the supplier term gates sellability. Read once, because every screen must agree with the
    /// worker: flipping it between the shop and fulfilment would make the shop advertise stock the worker
    /// then refuses, or hide stock the worker would have sold.
    /// </summary>
    private readonly bool _expiryEnabled;

    public PackageStockAvailability(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _expiryEnabled = configuration.GetValue("VoucherExpiration:Enabled", true);
    }

    /// <summary>
    /// Which stock pool this caller may see: test vouchers for a QA account, real vouchers for everyone
    /// else, and real ones for an anonymous visitor (who is not a QA account by definition).
    /// </summary>
    /// <remarks>
    /// Read from the database rather than the token, and only when the caller actually sent one: an
    /// anonymous catalog read is the common case (marketing site, pre-login browsing) and must not pay
    /// for a users lookup. A missing or soft-deleted row falls back to the real pool, which is the safe
    /// direction — the caller then sees what the public storefront shows.
    /// </remarks>
    public async Task<bool> IsTestAccountAsync(Guid? callerUserId, CancellationToken cancellationToken = default)
    {
        if (callerUserId is null || callerUserId.Value == Guid.Empty)
            return false;

        return await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId.Value && u.IsQaAccount, cancellationToken);
    }

    /// <summary>
    /// Every <see cref="StockKey"/> the given customer can buy today, as one grouped query. Used to annotate a
    /// whole catalog rather than asking per package.
    /// </summary>
    public async Task<HashSet<StockKey>> AvailableKeysAsync(bool isTestAccount, CancellationToken cancellationToken = default)
    {
        var rows = await SellableKeys(isTestAccount).ToListAsync(cancellationToken);
        return rows.ToHashSet();
    }

    /// <summary>
    /// The sellable stock keys, restricted to <paramref name="wanted"/> — one query for a whole cart,
    /// where probing per line would mean up to fifty identical round trips.
    /// </summary>
    public async Task<HashSet<StockKey>> AvailableKeysAsync(
        IReadOnlyCollection<StockKey> wanted,
        bool isTestAccount,
        CancellationToken cancellationToken = default)
    {
        if (wanted.Count == 0)
            return [];

        // Narrowed by IN-lists so Postgres can still walk the
        // (brand, fuel_type_id, liters, status) index instead of grouping the whole table.
        var rows = await SellableKeys(isTestAccount)
            .Where(k => wanted.Select(w => w.Brand).Contains(k.Brand)
                     && wanted.Select(w => w.FuelTypeId).Contains(k.FuelTypeId)
                     && wanted.Select(w => w.Liters).Contains(k.Liters))
            .ToListAsync(cancellationToken);

        var available = rows.Select(k => new StockKey(k.Brand, k.FuelTypeId, k.Liters)).ToHashSet();

        // The three IN-lists are independent, so the DB can hand back a cross product the cart never
        // asked about. Only the exact keys count.
        return available.Where(wanted.Contains).ToHashSet();
    }

    /// <summary>
    /// Whether at least one sellable voucher backs this exact package for this customer. One row, answered
    /// through the <c>(brand, fuel_type_id, liters, status)</c> index.
    /// </summary>
    public Task<bool> IsInStockAsync(
        bool isTestAccount,
        string stationId,
        string fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var brand = stationId.ToLower();

        return _context.FuelVouchers
            .AsNoTracking()
            .AnyAsync(
                v => v.Status == VoucherStatus.Available
                  && v.Brand.ToLower() == brand
                  && v.FuelTypeId == fuelTypeId
                  && v.Liters == liters
                  && v.IsTestData == isTestAccount
                  && (!_expiryEnabled || v.SupplierExpirationDate >= today),
                cancellationToken);
    }

    private IQueryable<StockKey> SellableKeys(bool isTestAccount)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.IsTestData == isTestAccount
                     && (!_expiryEnabled || v.SupplierExpirationDate >= today))
            .Select(v => new StockKey(v.Brand.ToLower(), v.FuelTypeId, v.Liters))
            .Distinct();
    }
}