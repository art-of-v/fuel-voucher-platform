using FuelFlow.API.Features.Orders;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Read-only term quote for one cart line: the per-term discount and the price the customer would pay.
/// </summary>
/// <remarks>
/// Optimistic by design, mirroring the renewal quote. Nothing is reserved and no price is frozen here;
/// <see cref="BulkCheckoutCommandHandler"/> recomputes server-side at checkout and refuses a line that
/// would sell below cost. So a manager editing the ladder mid-session can change the figures the picker
/// shows, but never the figures charged.
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
        string stationId, string fuelTypeId, decimal liters, CancellationToken cancellationToken = default)
    {
        var config = await _settings.GetVoucherTermConfigAsync(cancellationToken);

        // A missing package is not an error: the catalog can change between browsing and checkout, and the
        // line gets rejected properly at checkout. Returning the bare ladder lets the picker render without
        // prices rather than failing the whole screen.
        var package = await _context.FuelPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.StationId == stationId && p.FuelTypeId == fuelTypeId && p.Liters == liters,
                cancellationToken);

        // The below-cost guard needs the fuel's opt-in flag, so read it once with the package. A fuel that
        // is allowed to sell under cost can still offer the tier.
        var allowBelowCost = package is null || await _context.FuelTypes
            .AsNoTracking()
            .Where(f => f.Id == fuelTypeId)
            .Select(f => f.AllowBelowCost)
            .FirstOrDefaultAsync(cancellationToken);

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
                            && linePrice > 0m;

            terms.Add(new TermQuoteItem(tier.Term.Code(), discount, perLiter, linePrice, liters, available));
        }

        return TermQuoteResponse.From(config, terms);
    }
}