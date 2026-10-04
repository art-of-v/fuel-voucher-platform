using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal.Checkout;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Renewal.Quote;

/// <summary>
/// Read-only pricing/eligibility preview for a batch of the caller's vouchers. Mirrors the gates in
/// <see cref="RenewalCheckoutCommandHandler"/> but mutates nothing and never rejects the whole request
/// for a per-voucher problem — an ineligible voucher or an unbuyable tier comes back flagged, so the
/// client can render the term picker with the right rows disabled BEFORE the customer pays. The
/// authoritative gate (distinct stock per line, atomic claim) still runs at checkout; this preview is
/// deliberately optimistic about cross-voucher stock contention (it answers "is ANY stock reaching
/// this term", not "can every replace line in the batch be filled at once").
/// </summary>
public sealed class RenewalQuoteCommandHandler
{
    /// <summary>Matches the checkout batch cap; keeps the preview bounded.</summary>
    private const int MaxItems = 50;

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;

    public RenewalQuoteCommandHandler(ApplicationDbContext context, RuntimeSettingsService settings)
    {
        _context = context;
        _settings = settings;
    }

    public async Task<RenewalQuoteResponse> HandleAsync(
        RenewalQuoteCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.UserId == null || command.UserId == Guid.Empty)
            throw new ArgumentException("UserId is required", nameof(command));

        if (command.VoucherIds.Count > MaxItems)
            throw new VoucherRenewalException("too_many_items", $"A renewal batch may contain at most {MaxItems} vouchers.");

        var userId = command.UserId.Value;
        var config = await _settings.GetVoucherRenewalConfigAsync(cancellationToken);

        var response = new RenewalQuoteResponse
        {
            Enabled = config.Enabled,
            ThresholdDays = config.TriggerThresholdDays
        };

        // Feature off (fail-safe) or nothing to quote → client hides the affordance / shows nothing.
        if (!config.Enabled || command.VoucherIds.Count == 0)
            return response;

        var distinctIds = command.VoucherIds.Distinct().ToList();
        var sources = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => distinctIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Resolve fuel display names for every fuel type in one query.
        var fuelTypeIds = sources.Select(v => v.FuelTypeId).Distinct().ToList();
        var fuelNames = await _context.FuelTypes
            .AsNoTracking()
            .Where(ft => fuelTypeIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        // For replace lines we need, per (provider, fuel, litres) group, the best stock expiry in hand.
        // Gather the groups of eligible replace vouchers, then one lookup each.
        var replaceGroups = new Dictionary<(string Provider, string FuelTypeId, decimal Liters), DateOnly?>();

        foreach (var voucherId in command.VoucherIds)
        {
            var source = sources.FirstOrDefault(v => v.Id == voucherId);
            var quote = new RenewalVoucherQuote { VoucherId = voucherId };

            // Ownership: only the caller's own voucher (soft-deleted rows are already filtered out).
            if (source == null || source.AssignedToUserId != userId)
            {
                quote.Eligible = false;
                quote.IneligibleReason = "not_your_voucher";
                response.Vouchers.Add(quote);
                continue;
            }

            quote.Provider = source.Provider;
            quote.FuelTypeId = source.FuelTypeId;
            quote.FuelName = fuelNames.TryGetValue(source.FuelTypeId, out var name) && !string.IsNullOrEmpty(name)
                ? name
                : source.FuelTypeId;
            quote.Liters = source.Liters;
            quote.ExpirationDate = source.CustomerExpirationDate;

            // The branch depends on which tiers can clear the provider-term ceiling, so the offered
            // ladder has to be resolved before the branch - not after.
            var offeredTerms = VoucherRenewalTerms.All
                .Where(term => config.Tier(term) is { IsOfferable: true })
                .ToList();

            if (!VoucherRenewalEligibility.TryResolveBranch(
                    source.Status,
                    source.CustomerExpirationDate,
                    source.ProviderExpirationDate,
                    today,
                    config.TriggerThresholdDays,
                    offeredTerms,
                    out var branch))
            {
                quote.Eligible = false;
                quote.IneligibleReason = "not_renewable";
                response.Vouchers.Add(quote);
                continue;
            }

            quote.Eligible = true;
            quote.Branch = branch == VoucherRenewalBranch.Extend ? "extend" : "replace";

            DateOnly? bestStockExpiry = null;
            if (branch == VoucherRenewalBranch.Replace)
                bestStockExpiry = await BestStockExpiryAsync(replaceGroups, source, cancellationToken);

            foreach (var term in VoucherRenewalTerms.All)
            {
                var tier = config.Tier(term);
                var offerable = tier is not null && tier.IsOfferable;
                var price = offerable
                    ? VoucherRenewalPricing.LineAmountUah(source.Liters, tier!.RatePerLiterUah)
                    : 0;

                bool available;
                string? unavailableReason;

                if (!offerable)
                {
                    available = false;
                    unavailableReason = "not_offerable";
                }
                else if (branch == VoucherRenewalBranch.Replace)
                {
                    var minExpiration = VoucherRenewalEligibility.MinStockExpirationForReplace(today, term);
                    available = bestStockExpiry.HasValue && bestStockExpiry.Value >= minExpiration;
                    unavailableReason = available ? null : "no_stock";
                }
                else if (VoucherRenewalEligibility.CanExtend(
                             source.CustomerExpirationDate, source.ProviderExpirationDate, term))
                {
                    // Extend needs no stock - only room left in the voucher's real term.
                    available = true;
                    unavailableReason = null;
                }
                else
                {
                    // The manager offers this tier, but the supplier's voucher does not have the life
                    // to back it. We cannot invent validity, so it is not for sale on this voucher.
                    available = false;
                    unavailableReason = "provider_term_exhausted";
                }

                quote.Terms.Add(new RenewalTermQuote
                {
                    Term = term.Code(),
                    PriceUah = price,
                    Available = available,
                    UnavailableReason = unavailableReason
                });
            }

            response.Vouchers.Add(quote);
        }

        return response;
    }

    /// <summary>
    /// Best (latest) expiry among Available stock matching a source voucher's provider/fuel/litres, or
    /// null when nothing is in stock. Memoised per group so a batch of same-group replace lines costs
    /// one query. A tier is buyable when this reaches at least today + term.
    /// </summary>
    private async Task<DateOnly?> BestStockExpiryAsync(
        Dictionary<(string Provider, string FuelTypeId, decimal Liters), DateOnly?> cache,
        FuelVoucher source,
        CancellationToken cancellationToken)
    {
        var key = (source.Provider.ToLower(), source.FuelTypeId, source.Liters);
        if (cache.TryGetValue(key, out var cached))
            return cached;

        var best = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Provider.ToLower() == key.Item1
                     && v.FuelTypeId == source.FuelTypeId
                     && v.Liters == source.Liters)
            .OrderByDescending(v => v.ProviderExpirationDate)
            .Select(v => (DateOnly?)v.ProviderExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);

        cache[key] = best;
        return best;
    }
}
