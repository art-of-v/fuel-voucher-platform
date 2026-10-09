using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.BackgroundJobs;

/// <summary>
/// Keeps the OKKO catalogue's pump price (колонка) in step with what OKKO charges.
/// <para>
/// The customer-facing prices, the struck "до" price and every per-litre saving shown in the app are
/// derived from the pump price, so a stale pump means the app quietly shows a discount against a price
/// the customer can no longer get at the pump. Fetching this by hand does not scale: the list changes
/// a few times a week and nobody notices the day it stops being right.
/// </para>
/// <para>
/// Failure policy: keep the last known prices. A scrape failure logs and returns without touching the
/// catalogue, so an outage at OKKO's end (or a layout change) leaves yesterday's prices in place —
/// visibly stale, but not blanked. Blanking them would turn an outage into "no price shown to
/// customers" and would silently switch pricing back to cost-plus.
/// </para>
/// <para>
/// Every fuel whose pump actually moves is recorded by
/// <see cref="ImportOkkoPumpPricesCommandHandler"/> as a <c>PumpPriceImported</c> audit event, so an
/// unattended price change is as traceable as a typed one.
/// </para>
/// </summary>
public sealed class OkkoPriceSyncService
{
    /// <summary>Actor recorded on audit rows written by the scheduler — there is no person behind them.</summary>
    public const string SyncActorName = "OKKO price sync";

    private readonly IOkkoPriceClient _client;
    private readonly ImportOkkoPumpPricesCommandHandler _import;
    private readonly OkkoPriceOptions _options;
    private readonly ILogger<OkkoPriceSyncService> _logger;

    public OkkoPriceSyncService(
        IOkkoPriceClient client,
        ImportOkkoPumpPricesCommandHandler import,
        IOptions<OkkoPriceOptions> options,
        ILogger<OkkoPriceSyncService> logger)
    {
        _client = client;
        _import = import;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SyncPricesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "OKKO price sync skipped (OkkoPrice:Enabled=false); the catalogue keeps its current pump prices");
            return;
        }

        IReadOnlyList<OkkoPumpPrice> prices;
        try
        {
            prices = await _client.FetchAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberate: no fallback write. Today's fetch failing must not be able to change a price.
            _logger.LogError(
                ex, "OKKO price sync failed to fetch prices; keeping the last known pump prices");
            return;
        }

        OkkoPumpApplyResult result;
        try
        {
            result = await _import.ApplyAsync(
                prices, dryRun: false, actingUserId: Guid.Empty, SyncActorName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OKKO price sync fetched {Count} price(s) but failed to apply them",
                prices.Count);
            return;
        }

        if (result.FuelsChanged == 0)
        {
            _logger.LogDebug(
                "OKKO price sync: {Matched} fuel(s) matched, no pump price changed", result.FuelsMatched);
        }
        else
        {
            _logger.LogInformation(
                "OKKO price sync: {Changed} of {Matched} fuel(s) changed, {Packages} package(s) repriced",
                result.FuelsChanged, result.FuelsMatched, result.PackagesRepriced);
        }

        // A pump low enough to undercut our own cost is refused by the importer (as it is on the
        // manual path). Surfaced here because, unlike a typed rejection, nobody is watching for it.
        if (result.BelowCostSkipped.Count > 0)
        {
            _logger.LogError(
                "OKKO price sync refused {Count} fuel(s) whose published pump price would sell below cost: " +
                "{Fuels}. They keep their previous pump price and will stay off sale until reviewed.",
                result.BelowCostSkipped.Count, string.Join(", ", result.BelowCostSkipped));
        }
    }
}