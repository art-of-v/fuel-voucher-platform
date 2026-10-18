using FuelFlow.API.Features.Orders;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
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

    public TermQuoteQueryHandler(ApplicationDbContext context, RuntimeSettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public async Task<TermQuoteResponse> HandleAsync(
        bool isTestAccount,
        string BrandId, string fuelTypeId, decimal liters, CancellationToken cancellationToken = default)
    {
        var config = await _settings.GetVoucherTermConfigAsync(cancellationToken);
        var pool = DataKinds.ForPool(isTestAccount);

        // A missing package is not an error: the catalog can change between browsing and checkout, and the
        // line gets rejected properly at checkout. Returning the bare ladder lets the picker render without
        // prices rather than failing the whole screen.
var row = await (
            from offer in _context.FuelOffers.AsNoTracking()
            join nominal in _context.FuelNominals.AsNoTracking() on offer.FuelTypeId equals nominal.FuelTypeId
            where offer.BrandId == BrandId
                  && offer.FuelTypeId == fuelTypeId
                  && nominal.Liters == liters
                  && offer.DataKind == pool
            select new { Offer = offer, Fuel = offer.FuelType! })
            .FirstOrDefaultAsync(cancellationToken);

        var package = row is null ? null : SellablePackage.From(row.Offer, row.Fuel, liters);

        // The below-cost guard needs the fuel's opt-in flag. The fuel is already joined above - the
        // package's margin is its margin - so this no longer costs a second query per quote.
        var allowBelowCost = package is null || row!.Fuel.AllowBelowCost;

        // The ladder must not offer a term the Brand cannot honour. Charging the long-term price and
        // then clamping the delivered validity down to the stock's paper term is exactly the mis-sell this
        // gate exists to prevent, so a term with no backing stock is simply not on the ladder.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var bestStockExpiration = await TermStockCoverage.BestStockExpirationAsync(
            _context, BrandId, fuelTypeId, liters, cancellationToken);

        var terms = new List<TermQuoteItem>();
        foreach (var tier in config.Tiers)
        {
            var discount = tier.IsOfferable ? tier.DiscountPerLiterUah : 0m;
            var perLiter = package is null ? (decimal?)null : ServerPricing.DiscountedPricePerLiter(package, discount);
            var linePrice = package is null ? 0m : ServerPricing.PackagePrice(package, liters, discount);

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
