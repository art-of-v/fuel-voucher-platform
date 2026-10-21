using FuelFlow.Features.Pricing.BackfillBrandFuelCodes;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Persistence;
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
    private readonly ImportOkkoCatalogueCommandHandler _catalogue;
    private readonly BackfillBrandFuelCodesCommandHandler _backfill;
    private readonly ApplicationDbContext _context;
    private readonly OkkoPriceOptions _options;
    private readonly ILogger<OkkoPriceSyncService> _logger;

    public OkkoPriceSyncService(
        IOkkoPriceClient client,
        ImportOkkoPumpPricesCommandHandler import,
        ImportOkkoCatalogueCommandHandler catalogue,
        BackfillBrandFuelCodesCommandHandler backfill,
        ApplicationDbContext context,
        IOptions<OkkoPriceOptions> options,
        ILogger<OkkoPriceSyncService> logger)
    {
        _client = client;
        _import = import;
        _catalogue = catalogue;
        _backfill = backfill;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SyncPricesAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "OKKO price sync skipped (OkkoPrice:Enabled=false); the catalogue keeps its current pump prices");
            return;
        }

        var prices = await FetchAsync(ct);
        if (prices is null)
        {
            return;
        }

        await PriceAsync(prices, ct);
    }

    /// <summary>
    /// The weekly check: whether OKKO sells a grade we do not carry, and then the price of it.
    /// </summary>
    /// <remarks>
    /// A new grade is news on a scale of weeks, and creating one is a decision - it puts a row in
    /// the catalogue and a size on the shelf - so it belongs to a job that runs weekly rather than to
    /// the one that moves prices every fifteen minutes.
    /// </remarks>
    public async Task SyncCatalogueAndPricesAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "OKKO catalogue sync skipped (OkkoPrice:Enabled=false); the catalogue keeps the grades it has");
            return;
        }

        var prices = await FetchAsync(ct);
        if (prices is null)
        {
            return;
        }

        await CatalogueAsync(prices, ct);
        await PriceAsync(prices, ct);
    }

    private async Task<IReadOnlyList<OkkoPumpPrice>?> FetchAsync(CancellationToken ct)
    {
        IReadOnlyList<OkkoPumpPrice> prices;
        try
        {
            prices = await _client.FetchAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberate: no fallback write. Today's fetch failing must not be able to change a price.
            _logger.LogError(
                ex, "OKKO price sync failed to fetch prices; keeping the last known pump prices");
            return null;
        }

        return prices;
    }

    /// <summary>
    /// The catalogue half: the grades OKKO sells that we do not carry.
    /// </summary>
    private async Task CatalogueAsync(IReadOnlyList<OkkoPumpPrice> prices, CancellationToken ct)
    {
        // Before pricing, because a fuel that does not exist yet cannot be priced: the pump has
        // nowhere to live until the fuel does. Its own try/catch because a catalogue failure must not
        // cost us today's prices - the two write disjoint columns.
        try
        {
            var catalogue = await _catalogue.ApplyAsync(
                new ImportOkkoCatalogueCommand(prices, DryRun: false),
                Guid.Empty, SyncActorName, ct);

            if (catalogue.Created > 0)
            {
                // Information, not Debug: the catalogue grew and nobody typed it. Each created fuel also
                // carries a FuelCreatedByImport audit row, so this is the second place it is recorded.
                _logger.LogInformation(
                    "OKKO sync added {Count} fuel(s) the catalogue did not have: {Fuels}. They are on no "
                    + "offer for sale until a margin is set - each is created with the published pump "
                    + "price only.",
                    catalogue.Created, string.Join(", ", catalogue.CreatedFuels.Select(f => $"{f.Name} [{f.BrandFuelCode}]")));
            }

            if (catalogue.GradesHeld.Count > 0)
            {
                // Information, not Warning: "the sheet calls it X, we call it Y" is a fact about the two
                // vocabularies, and it is the only thing that can give the operator the brand's code.
                _logger.LogInformation(
                    "OKKO publishes {Count} grade(s) we already sell under our own name: {Grades}. "
                    + "Nothing was created - one row per grade, whatever it is called. Giving our fuel the "
                    + "brand's code is what makes it resolve exactly.",
                    catalogue.GradesHeld.Count,
                    string.Join(" | ", catalogue.GradesHeld.Select(g => $"{g.SiteFuelCode} = \"{g.OurFuelName}\"")));
            }

            if (catalogue.NameCollisions.Count > 0)
            {
                // Warning: this one needs a human to rename something, and nothing will resolve it.
                _logger.LogWarning(
                    "OKKO publishes {Count} grade(s) whose canonical name is already taken by a fuel of "
                    + "another grade: {Collisions}. Nothing was created; one of the two has to be renamed.",
                    catalogue.NameCollisions.Count,
                    string.Join(" | ", catalogue.NameCollisions.Select(c => $"{c.SiteFuelCode} wants \"{c.Name}\"")));
            }

            if (catalogue.UnclassifiedSiteFuels.Count > 0)
            {
                _logger.LogInformation(
                    "OKKO published {Count} product(s) we do not sell as a grade (jet fuel, AdBlue): "
                    + "{Codes}. Nothing was created for them.",
                    catalogue.UnclassifiedSiteFuels.Count,
                    string.Join(", ", catalogue.UnclassifiedSiteFuels));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ForgetFailedWrites();
            _logger.LogError(ex, "OKKO sync failed to add missing fuels; prices are still applied");
        }
    }

    /// <summary>
    /// The price half: the codes our fuels match by, and what OKKO charges.
    /// </summary>
    private async Task PriceAsync(IReadOnlyList<OkkoPumpPrice> prices, CancellationToken ct)
    {
        // Before pricing, and after the catalogue half above: the price step matches a fuel by its code
        // where it has one, so the fuels an operator typed — which have none — are what is left
        // resolving by category. Filling them from the same sheet is idempotent and, once done, does
        // nothing; a fuel the catalogue import created a minute ago already carries its own code.
        try
        {
            var backfill = await _backfill.ApplyAsync(
                new BackfillBrandFuelCodesCommand(prices, DryRun: false),
                Guid.Empty, SyncActorName, ct);

            if (backfill.Filled > 0)
            {
                _logger.LogInformation(
                    "OKKO sync matched {Count} fuel(s) to a published code: {Fuels}. Until now they "
                    + "resolved by category, which cannot tell two fuels of one grade apart.",
                    backfill.Filled, string.Join(", ", backfill.Fills.Select(f => $"{f.Name} → {f.Code}")));

                if (backfill.Skipped.Count > 0)
                {
                    // Information, not Warning: a grade with no published code, or several, is a fact
                    // about the catalogue rather than a fault, and it will say the same thing every
                    // fifteen minutes until somebody codes it by hand.
                    _logger.LogInformation(
                        "OKKO sync left {Count} fuel(s) uncoded: {Fuels}. A fuel with a code resolves "
                        + "exactly; without one, two fuels of the same grade are indistinguishable.",
                        backfill.Skipped.Count,
                        string.Join(" | ", backfill.Skipped.Select(s => $"{s.Name} ({s.Reason})")));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ForgetFailedWrites();
            _logger.LogError(ex, "OKKO sync failed to match fuels to published codes; prices are still applied");
        }

        OkkoPumpApplyResult result;
        try
        {
            result = await _import.ApplyAsync(
                prices, dryRun: false, actingUserId: Guid.Empty, SyncActorName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ForgetFailedWrites();
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
    }

    /// <summary>
    /// Drops the rows of a step that failed while saving.
    /// <para>
    /// The three handlers share this context, because Hangfire resolves one scope per tick. A step that
    /// is refused by a unique index flushes nothing, yet the entities it added stay attached — so the
    /// next step's own save tries to insert them a second time, and the failure it reports is about the
    /// previous step's row. That is how a clash on a fuel name in the catalogue step surfaced as an
    /// unrelated clash on a brand code in the backfill step, every fifteen minutes, with neither error
    /// naming the real cause. Nothing is lost by clearing: the failed step wrote nothing, and the next
    /// tick redoes it.
    /// </para>
    /// </summary>
    private void ForgetFailedWrites() => _context.ChangeTracker.Clear();
}