using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Availability;

/// <summary>
/// The composite a voucher is matched on, and therefore the composite a package's stock is counted by:
/// brand + fuel type + nominal. <see cref="Brand"/> is always lower-cased, because the catalog and
/// order lines carry <c>okko</c> while imported stock carries <c>OKKO</c> and every matcher compares
/// case-insensitively.
/// </summary>
public readonly record struct StockKey(string Brand, Guid FuelTypeId, decimal Liters)
{
    public static StockKey For(string brand, Guid fuelTypeId, decimal liters)
        => new(brand.ToLowerInvariant(), fuelTypeId, liters);
}

/// <summary>
/// One definition of "this package can actually be sold", shared by the store front that advertises it
/// and the checkout that charges for it.
/// </summary>
/// <remarks>
/// This mirrors <c>FulfillmentService.FindMatchingVoucherAsync</c> key for key — <see cref="VoucherStatus.Available"/>
/// only (imported stock is not sellable until a manager activates it, which in turn requires the batch cost),
/// the real stock pool, and the supplier term. Deliberately the same rule on both sides of the
/// counter: a package the shop advertises as available and then refuses to sell is the bug this class exists
/// to prevent, and a package it hides while fulfilment would happily hand the voucher over wastes a sale.
///
/// The predicate is an offer to sell, not a reservation. Nothing is held here, so two customers can still race for
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

    public PackageStockAvailability(
        ApplicationDbContext context,
        IConfiguration configuration)
    {
        _context = context;
        _expiryEnabled = configuration.GetValue("VoucherExpiration:Enabled", true);
    }

    /// <summary>
    /// The <see cref="FuelPackage"/> that will actually be charged for one (Brand, fuel, litres)
    /// line, or <see langword="null"/> when the real catalogue has no package for that triple.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single implementation shared with the storefront, and it exists because the two
    /// sides had drifted into reading different rows for the same key. Checkout used to resolve the
    /// package with a bare <c>FirstOrDefaultAsync</c> on <c>(Brand, fuel, liters)</c>: no
    /// <c>DataKind</c> filter and no <c>OrderBy</c>.
    /// </para>
    /// <para>
    /// Both omissions matter because a triple is not unique. A rehearsal nominal can mirror a real
    /// one on the <em>same</em> Brand, fuel and size (the admin write path creates them with
    /// <c>isTestData</c>), so an unordered <c>FirstOrDefaultAsync</c> over two rows returns whichever
    /// one Postgres happens to produce first. Filtering on the real pool and ordering
    /// deterministically is what keeps a customer from being charged off a row the shop never
    /// showed.
    /// </para>
    /// <para>
    /// The pool is always <see cref="DataKinds.Real"/>: every account buys from the real shop,
    /// including the App Store QA account. Rehearsal rows remain visible to the admin screens that
    /// create and clean them up, never to a buyer.
    /// </para>
    /// </remarks>
    public async Task<SellablePackage?> FindPackageAsync(
        Guid brandId,
        Guid fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        // The size is the fuel's, not a row's: the fuel names the price and the nominal says this
        // size is allowed for sale. fuel_nominals is keyed (fuel_type_id, data_kind, liters), so
        // exactly one package can match.
        var row = await (
            from fuel in _context.FuelTypes.AsNoTracking()
            from nominal in _context.FuelNominals.AsNoTracking()
            where nominal.FuelTypeId == fuel.Id
            // The slug is joined rather than resolved afterwards: the caller matches vouchers and order
            // lines by brand TEXT, so a package that came back without one could not be stocked or
            // ordered.
            join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
            where fuel.BrandId == brandId
                  && fuel.Id == fuelTypeId
                  && nominal.Liters == liters
                  // The real pool, on the nominal: a rehearsal size is never offered for sale.
                  && nominal.DataKind == DataKinds.Real
            select new { Fuel = fuel, DataKind = nominal.DataKind, BrandSlug = brand.Slug })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : SellablePackage.From(row.Fuel, row.DataKind, liters, row.BrandSlug);
    }

    /// <summary>
    /// The same resolution for a whole cart, so bulk checkout does not repeat the query per line.
    /// Keyed by the same <see cref="StockKey"/> the stock gate uses, which is what lets a line be
    /// priced off exactly the package its stock was checked against.
    /// </summary>
    public async Task<Dictionary<StockKey, SellablePackage>> FindPackagesAsync(
        IReadOnlyCollection<StockKey> wanted,
        CancellationToken cancellationToken = default)
    {
        if (wanted.Count == 0)
            return [];

        // Narrowed with separate IN-lists on the entity rather than with
        // `wanted.Contains(StockKey.For(fuel.BrandId, fuel.Id, nominal.Liters))`, which
        // cannot be translated - the same wall SellableKeys documents below, hit for the same reason.
        // Separate lists hand back a cross product the cart never asked about, so the resolved
        // dictionary is filtered back down to `wanted` after materialisation.
        var brands = wanted.Select(k => k.Brand).Distinct().ToArray();
        var fuelTypeIds = wanted.Select(k => k.FuelTypeId).Distinct().ToArray();
        var literSizes = wanted.Select(k => k.Liters).Distinct().ToArray();

var rows = await (
            from fuel in _context.FuelTypes.AsNoTracking()
            from nominal in _context.FuelNominals.AsNoTracking()
            where nominal.FuelTypeId == fuel.Id
            // The cart names its brands by slug, so the narrowing below compares against the
            // network's own code rather than an id the cart never had.
            join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
            where nominal.DataKind == DataKinds.Real
                  && brands.Contains(brand.Slug.ToLower())
                  && fuelTypeIds.Contains(fuel.Id)
                  && literSizes.Contains(nominal.Liters)
            // Same precedence as the single-line path, so one line of a cart cannot resolve to a
            // different package than the same line ordered on its own. Both keys are unique, so this
            // ordering is free belt-and-braces rather than a tie-break anything depends on.
            orderby fuel.Id, nominal.Liters
            select new { Fuel = fuel, DataKind = nominal.DataKind, Liters = nominal.Liters, BrandSlug = brand.Slug })
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<StockKey, SellablePackage>(wanted.Count);
        foreach (var row in rows)
        {
var key = StockKey.For(row.BrandSlug, row.Fuel.Id, row.Liters);
            if (!wanted.Contains(key))
                continue;

            // First write wins, and the ordering above means "first" is the same package the
            // single-line path would have taken.
            resolved.TryAdd(key, SellablePackage.From(row.Fuel, row.DataKind, row.Liters, row.BrandSlug));
        }

        return resolved;
    }

    /// <summary>
    /// Every <see cref="StockKey"/> the given customer can buy today, as one grouped query. Used to annotate a
    /// whole catalog rather than asking per package.
    /// </summary>
    public async Task<HashSet<StockKey>> AvailableKeysAsync(CancellationToken cancellationToken = default)
    {
        var rows = await SellableKeys().ToListAsync(cancellationToken);
        return rows.ToHashSet();
    }

    /// <summary>
    /// The fuels the customer can buy today: every fuel with at least one size that has live stock
    /// behind it.
    /// </summary>
    /// <remarks>
    /// The same stock the packages endpoint counts, asked a different question. This one is for a
    /// screen that lists FUELS rather than packages - a brand's page, where the customer asks "can I
    /// buy this fuel" and not "can I buy this exact size of it".
    /// </remarks>
    public async Task<HashSet<Guid>> AvailableFuelIdsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await SellableKeys().ToListAsync(cancellationToken);
        return rows.Select(r => r.FuelTypeId).ToHashSet();
    }

    /// <summary>
    /// The sellable stock keys, restricted to <paramref name="wanted"/> — one query for a whole cart,
    /// where probing per line would mean up to fifty identical round trips.
    /// </summary>
    public async Task<HashSet<StockKey>> AvailableKeysAsync(
        IReadOnlyCollection<StockKey> wanted,
        CancellationToken cancellationToken = default)
    {
        if (wanted.Count == 0)
            return [];

        // Narrowed by IN-lists so Postgres can still walk the
        // (brand, fuel_type_id, liters, status) index instead of grouping the whole table - but
        // narrowed inside SellableKeys, on the entity, ahead of the projection. See there for why.
        var rows = await SellableKeys(
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
        string brandSlug,
        Guid fuelTypeId,
        decimal liters,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        // Vouchers still carry the network by text, so this gate - unlike the fuel lookups above -
        // is matched on the slug rather than on a uuid.
        var brand = brandSlug.ToLower();

        return _context.FuelVouchers
            .AsNoTracking()
            .AnyAsync(
                v => v.Status == VoucherStatus.Available
                  && v.Brand == brand
                  && v.FuelTypeId == fuelTypeId
                  && v.Liters == liters
                  && v.DataKind == DataKinds.Real
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
        string[]? brands = null,
        Guid[]? fuelTypeIds = null,
        decimal[]? literSizes = null)
    {
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);

        var query = _context.StockSummary
            .AsNoTracking()
            .Where(s => s.Status == VoucherStatus.Available
                     && s.DataKind == DataKinds.Real
                     && !s.IsDeleted
                     && (!_expiryEnabled || s.SupplierExpirationDate >= today)
                     && s.VoucherCount > 0);

        if (brands != null)
        {
            query = query.Where(s => brands.Contains(s.Brand)
                                  && fuelTypeIds!.Contains(s.FuelTypeId)
                                  && literSizes!.Contains(s.Liters));
        }

        // The brand is projected RAW, not case-folded in SQL. `lower(brand)` in a projection is
        // computed per row, so the index that covers (brand, fuel_type_id, liters, status) cannot
        // serve it as an index-only scan - it has to fetch the heap to fold the string. The
        // canonical form is the slug, and StockKey's contract is the lower-cased slug, so the fold
        // happens on the rows that come back.
        return query
            .Select(s => new { s.Brand, s.FuelTypeId, s.Liters })
            .Distinct()
            .Select(s => new StockKey(s.Brand.ToLowerInvariant(), s.FuelTypeId, s.Liters));
    }
}
