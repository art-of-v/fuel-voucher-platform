using FuelFlow.Features.Auth.QaTestAccess;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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
    /// The configured QA phone, already normalized, or <see langword="null"/> when QA test access is
    /// not wired into this host. Read once per call so the comparison below stays translatable -
    /// the alternative, calling a helper inside the <c>Where</c>, is a C# delegate the provider
    /// cannot translate.
    /// </summary>
    private readonly QaTestAccessService? _qaTestAccess;

    /// <summary>
    /// Whether the supplier term gates sellability. Read once, because every screen must agree with the
    /// worker: flipping it between the shop and fulfilment would make the shop advertise stock the worker
    /// then refuses, or hide stock the worker would have sold.
    /// </summary>
    private readonly bool _expiryEnabled;

    public PackageStockAvailability(
        ApplicationDbContext context,
        IConfiguration configuration,
        QaTestAccessService? qaTestAccess = null)
    {
        _context = context;
        _qaTestAccess = qaTestAccess;
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

        // QA is an OR, the same shape MonobankMerchantResolver.IsQa uses: flagged, or the
        // configured QA number.
        //
        // The flag alone was not enough. Deleting the QA account and signing up again mints a
        // fresh row for the same phone, and the flag was only ever set by the seeding migration -
        // so the re-registered account had none. Its QA code still worked and its token was
        // valid, and the tester saw an empty store: every screen said the catalogue was simply
        // empty, which reads as a broken app rather than as a missing flag. That is the exact
        // path App Review is told to perform on the demo account, so the phone arm has to carry
        // it on its own.
        var qaPhone = _qaTestAccess?.QaPhoneNumber;

        return await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == callerUserId.Value
                       && (u.IsQaAccount || u.PhoneNumber == qaPhone), cancellationToken);
    }

    /// <summary>
    /// The <see cref="FuelPackage"/> that will actually be charged for one (Brand, fuel, litres)
    /// line, or <see langword="null"/> when this caller's pool has no package for that triple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single implementation shared with the storefront, and it exists because the two
    /// sides had drifted into reading different rows for the same key. Checkout used to resolve the
    /// package with a bare <c>FirstOrDefaultAsync</c> on <c>(Brand, fuel, liters)</c>: no
    /// <c>DataKind</c> filter and no <c>OrderBy</c>.
    /// </para>
    /// <para>
    /// Both omissions matter because a triple is not unique. <c>scripts/seed-qa-catalogue.sql</c> gives
    /// every real package a QA mirror on the <em>same</em> Brand, fuel and nominal, so as shipped every
    /// sellable triple has exactly two live rows — measured on production 2026-10-14: 13 offers producing
    /// 58 rows, 58 flagged real and 58 flagged QA, with no duplicates inside either pool. An unordered
    /// <c>FirstOrDefaultAsync</c> over two rows returns whichever one Postgres happens to produce first.
    /// </para>
    /// <para>
    /// The price happens to be identical today only because the seed copies it, which is luck rather
    /// than a property. The moment a QA mirror is repriced independently, or an admin adds a second
    /// real package for a nominal, the customer is charged off an arbitrary row while the shop showed
    /// the other. Filtering on the caller's own pool and ordering deterministically makes that
    /// impossible instead of merely unlikely.
    /// </para>
    /// </remarks>
    public async Task<SellablePackage?> FindPackageAsync(
        bool isTestAccount,
        string BrandId,
        string fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
var pool = DataKinds.ForPool(isTestAccount);

        // The size is the fuel's, not a row's: the offer names the pool and the price, the nominal
        // says this size is allowed for sale. `UNIQUE (brand_id, fuel_type_id, data_kind)` on
        // fuel_offers times the fuel's own PK on fuel_nominals makes this key unique, so exactly one
        // package can match - which is what the ordering used to paper over when it was not.
        var row = await (
            from offer in _context.FuelOffers.AsNoTracking()
            join nominal in _context.FuelNominals.AsNoTracking() on offer.FuelTypeId equals nominal.FuelTypeId
            where offer.BrandId == BrandId
                  && offer.FuelTypeId == fuelTypeId
                && nominal.Liters == liters
// The caller's own pool, on BOTH sides: the size list is per pool, so a
            // rehearsal size must resolve through the rehearsal offer and not the live one, which is
            // the leak this guards. Filtering only the offer would let a live price answer for a
            // rehearsal size.
            && nominal.DataKind == pool
            && offer.DataKind == pool
            select new { Offer = offer, Fuel = offer.FuelType! })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : SellablePackage.From(row.Offer, row.Fuel, liters);
    }

    /// <summary>
    /// The same resolution for a whole cart, so bulk checkout does not repeat the query per line.
    /// Keyed by the same <see cref="StockKey"/> the stock gate uses, which is what lets a line be
    /// priced off exactly the package its stock was checked against.
    /// </summary>
    public async Task<Dictionary<StockKey, SellablePackage>> FindPackagesAsync(
        bool isTestAccount,
        IReadOnlyCollection<StockKey> wanted,
        CancellationToken cancellationToken = default)
    {
        if (wanted.Count == 0)
            return [];

        // Narrowed with separate IN-lists on the entity rather than with
        // `wanted.Contains(StockKey.For(offer.BrandId, offer.FuelTypeId, nominal.Liters))`, which
        // cannot be translated - the same wall SellableKeys documents below, hit for the same reason.
        // Separate lists hand back a cross product the cart never asked about, so the resolved
        // dictionary is filtered back down to `wanted` after materialisation.
        var brands = wanted.Select(k => k.Brand).Distinct().ToArray();
        var fuelTypeIds = wanted.Select(k => k.FuelTypeId).Distinct().ToArray();
        var literSizes = wanted.Select(k => k.Liters).Distinct().ToArray();
        var pool = DataKinds.ForPool(isTestAccount);

        var rows = await (
            from offer in _context.FuelOffers.AsNoTracking()
            join nominal in _context.FuelNominals.AsNoTracking() on offer.FuelTypeId equals nominal.FuelTypeId
where offer.DataKind == pool
            && nominal.DataKind == pool
            && brands.Contains(offer.BrandId.ToLower())
                  && fuelTypeIds.Contains(offer.FuelTypeId)
                  && literSizes.Contains(nominal.Liters)
            // Same precedence as the single-line path, so one line of a cart cannot resolve to a
            // different package than the same line ordered on its own. Both keys are unique, so this
            // ordering is free belt-and-braces rather than a tie-break anything depends on.
            orderby offer.Id, nominal.Liters
            select new { Offer = offer, Fuel = offer.FuelType!, Liters = nominal.Liters })
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<StockKey, SellablePackage>(wanted.Count);
        foreach (var row in rows)
        {
            var key = StockKey.For(row.Offer.BrandId, row.Offer.FuelTypeId, row.Liters);
            if (!wanted.Contains(key))
                continue;

            // First write wins, and the ordering above means "first" is the same package the
            // single-line path would have taken.
            resolved.TryAdd(key, SellablePackage.From(row.Offer, row.Fuel, row.Liters));
        }

        return resolved;
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
        // (brand, fuel_type_id, liters, status) index instead of grouping the whole table - but
        // narrowed inside SellableKeys, on the entity, ahead of the projection. See there for why.
        var rows = await SellableKeys(
                isTestAccount,
                wanted.Select(w => w.Brand).ToArray(),
                wanted.Select(w => w.FuelTypeId).ToArray(),
                wanted.Select(w => w.Liters).ToArray())
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
        string BrandId,
        string fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var brand = BrandId.ToLower();
        var pool = DataKinds.ForPool(isTestAccount);

        return _context.FuelVouchers
            .AsNoTracking()
            .AnyAsync(
                v => v.Status == VoucherStatus.Available
                  && v.Brand.ToLower() == brand
                  && v.FuelTypeId == fuelTypeId
                  && v.Liters == liters
                  && v.DataKind == pool
                  && (!_expiryEnabled || v.SupplierExpirationDate >= today),
                cancellationToken);
    }

    /// <summary>
    /// The distinct sellable <see cref="StockKey"/>s, optionally narrowed to a cart.
    /// </summary>
    /// <param name="brands">
    /// <see langword="null"/> means "no cart", i.e. the whole catalogue. Otherwise the brands,
    /// fuel types and nominals the cart asked for.
    /// </param>
    /// <remarks>
    /// The narrowing happens here, on the entity, and deliberately BEFORE the projection into
    /// <see cref="StockKey"/>. Filtering after it cannot be translated at all: EF reports
    /// <c>The LINQ expression 'brands.Contains(new StockKey(...).Brand)' could not be translated</c>,
    /// and that is the only error it raises - there is no compile-time warning and no unit test that
    /// catches it, because an in-memory provider evaluates the whole thing client-side and gets it
    /// right. Ahead of the projection the same predicate is ordinary column matching, which is what
    /// the IN-lists are for: Postgres answers it through the
    /// <c>(brand, fuel_type_id, liters, status)</c> index instead of grouping the whole table.
    /// <para>
    /// Shipped broken in 1.1.0 and caught by hand on the first real checkout:
    /// <c>POST /api/purchases/bulk</c> answered 500 for every customer from the moment the feature
    /// deployed, so nobody could buy fuel at all. Keep this parameter inside the entity query.
    /// </para>
    /// </remarks>
    private IQueryable<StockKey> SellableKeys(
        bool isTestAccount,
        string[]? brands = null,
        string[]? fuelTypeIds = null,
        decimal[]? literSizes = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pool = DataKinds.ForPool(isTestAccount);

        var query = _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.DataKind == pool
                     && (!_expiryEnabled || v.SupplierExpirationDate >= today));

        if (brands is not null)
        {
            query = query.Where(v => brands.Contains(v.Brand.ToLower())
                                  && fuelTypeIds!.Contains(v.FuelTypeId)
                                  && literSizes!.Contains(v.Liters));
        }

        return query
            .Select(v => new StockKey(v.Brand.ToLower(), v.FuelTypeId, v.Liters))
            .Distinct();
    }
}
