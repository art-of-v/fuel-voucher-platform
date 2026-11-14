using FuelFlow.API.Features.Orders;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Read-only term quote for one cart line: the per-term discount and the price the customer would pay.
/// </summary>
/// <remarks>
/// Optimistic by design, mirroring the renewal quote. Nothing is reserved and no price is frozen here;
/// <see cref="BulkCheckoutCommandHandler"/> recomputes server-side at checkout and refuses a line that
/// would sell below cost or on a term no stock can cover. So a manager editing the ladder mid-session can
/// change the figures the picker shows, but never the figures charged.
/// </remarks>
public sealed class TermQuoteQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly BrandSlugResolver _brands;

    public TermQuoteQueryHandler(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        BrandSlugResolver brands)
    {
        _context = context;
        _settings = settings;
        _brands = brands;
    }

    public async Task<TermQuoteResponse> HandleAsync(
        bool isTestAccount,
        Guid BrandId, Guid fuelTypeId, decimal liters, CancellationToken cancellationToken = default)
    {
        var config = await _settings.GetVoucherTermConfigAsync(cancellationToken);
        var pool = DataKinds.ForPool(isTestAccount);

        // A missing package is not an error: the catalog can change between browsing and checkout, and the
        // line gets rejected properly at checkout. Returning the bare ladder lets the picker render without
        // prices rather than failing the whole screen. An unpriced fuel is the same situation from the
        // customer's side: the catalogue published its pump price, but nobody has set a cost and a margin,
        // so there is no price to quote - and there is nothing the customer can do about that either.
var row = await (
        // The fuel, and whether it sells this size in this pool. There is no offer to join: the
        // price is the fuel's, so joining one could only ever bring a second opinion about it.
from fuel in _context.FuelTypes.AsNoTracking()
    where fuel.BrandId == BrandId
          && fuel.Id == fuelTypeId
          && !fuel.IsDeleted
    // The network's own code travels with the row: the stock check below matches by brand TEXT.
    join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
    from nominal in _context.FuelNominals.AsNoTracking()
    where nominal.FuelTypeId == fuel.Id
          && nominal.Liters == liters
          && nominal.DataKind == pool
    select new { Fuel = fuel, DataKind = nominal.DataKind, BrandSlug = brand.Slug })
        .FirstOrDefaultAsync(cancellationToken);

// The stock check below needs the network's NAME, not its id - see StockKey.
        var brandSlug = row?.BrandSlug;

        var package = row is null
            ? null
            : SellablePackage.From(row.Fuel, row.DataKind, liters, brandSlug ?? string.Empty);

        // The below-cost guard needs the fuel's opt-in flag. The fuel is already joined above - the
        // package's margin is its margin - so this no longer costs a second query per quote.
        var allowBelowCost = package is null || row!.Fuel.AllowBelowCost;

        // The ladder must not offer a term the Brand cannot honour. Charging the long-term price and
        // then clamping the delivered validity down to the stock's paper term is exactly the mis-sell this
        // gate exists to prevent, so a term with no backing stock is simply not on the ladder.
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        var bestStockExpiration = await TermStockCoverage.BestStockExpirationAsync(
            _context, brandSlug ?? string.Empty, fuelTypeId, liters, cancellationToken);

        var terms = new List<TermQuoteItem>();
        foreach (var tier in config.Tiers)
        {
            var discount = tier.IsOfferable ? tier.DiscountPerLiterUah : 0m;
            var perLiter = package is null ? (decimal?)null : ServerPricing.DiscountedPricePerLiter(package, discount);
            // No price to quote means no line price, not a fault: the ladder comes back with no prices
            // and nothing on it is buyable, which is the same answer a missing package gets. The line
            // is refused at checkout either way, so quoting it must not fail the screen.
            var linePrice = package is null
                ? 0m
                : ServerPricing.TryPackagePrice(package, liters, discount) ?? 0m;

            // Mirrors the checkout guard so the picker never offers a term that payment would reject.
            var belowCost = package is not null && !allowBelowCost && ServerPricing.IsBelowCost(package, discount);

            var available = tier.IsOfferable
                            && package is not null
                            && !belowCost
                            && linePrice > 0m
                            && TermStockCoverage.CanHonour(bestStockExpiration, today, tier.Term);

            terms.Add(new TermQuoteItem(tier.Term.Code(), discount, perLiter, linePrice, liters, available));
        }

        return TermQuoteResponse.From(config, terms);
    }
}
