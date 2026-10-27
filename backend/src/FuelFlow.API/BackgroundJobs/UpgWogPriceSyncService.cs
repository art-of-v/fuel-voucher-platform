using FuelFlow.Features.Pricing.ImportUpgWogPrices;
using FuelFlow.Features.Pricing.ScrapeUpgWogPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.BackgroundJobs;

/// <summary>
/// Keeps UPG's and WOG's pump prices in step with what they charge, every fifteen minutes.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="OkkoPriceSyncService"/>, for the two vendors that publish a page
/// rather than an API. The shape of the work is the same: fetch the sheet, hand it to the importer
/// that already exists, and let the importer create the grades it has never seen - with their
/// sizes - and record the price in the history.
/// </para>
/// <para>
/// <b>A failed fetch changes nothing.</b> A vendor page that is down, or that has changed shape,
/// leaves the last known prices in place. This importer does not archive a fuel the sheet stopped
/// carrying, so a bad fetch cannot empty the catalogue the way an empty sheet would on the OKKO
/// side - but the guard is still the first thing it does, because the importer refuses an
/// unreadable sheet rather than writing half of one.
/// </para>
/// <para>
/// <b>One brand failing does not stop the other.</b> Each is fetched, written and reported on its
/// own, because UPG and WOG are separate sites and one of them being down says nothing about the
/// other.
/// </para>
/// </remarks>
public sealed class UpgWogPriceSyncService
{
    /// <summary>Actor recorded on audit rows written by the scheduler — there is no person behind them.</summary>
    private const string SyncActorName = "upg-wog-price-sync";

    /// <summary>The brands this job serves, and the only ones its pages may name.</summary>
    public static readonly string[] SupportedBrands = ["upg", "wog"];

    private readonly IUpgWogPriceClient _client;
    private readonly ImportUpgWogPricesCommandHandler _import;
    private readonly ApplicationDbContext _context;
    private readonly UpgWogPriceOptions _options;
    private readonly ILogger<UpgWogPriceSyncService> _logger;

    public UpgWogPriceSyncService(
        IUpgWogPriceClient client,
        ImportUpgWogPricesCommandHandler import,
        ApplicationDbContext context,
        IOptions<UpgWogPriceOptions> options,
        ILogger<UpgWogPriceSyncService> logger)
    {
        _client = client;
        _import = import;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The fifteen-minute refresh: what the vendors charge, for the grades we already carry.
    /// </summary>
    public async Task SyncPricesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "UPG/WOG price sync skipped (UpgWogPrice:Enabled=false); the catalogue keeps its current pump prices");
            return;
        }

        foreach (var brand in SupportedBrands)
        {
            await SyncOneAsync(brand, createMissing: false, cancellationToken);
        }
    }

    /// <summary>
    /// The weekly check: whether the vendors sell a grade we do not carry.
    /// </summary>
    /// <remarks>
    /// A new grade is news on a scale of weeks, and creating one is a decision - it puts a row in the
    /// catalogue and a size on the shelf - so it belongs to a job that runs weekly rather than to the
    /// one that moves prices every fifteen minutes. The grades this carries are also the ones the
    /// price refresh prices, so the week the catalogue grows is the week the price reaches it.
    /// </remarks>
    public async Task SyncCatalogueAndPricesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "UPG/WOG catalogue sync skipped (UpgWogPrice:Enabled=false); the catalogue keeps the grades it has");
            return;
        }

        foreach (var brand in SupportedBrands)
        {
            await SyncOneAsync(brand, createMissing: true, cancellationToken);
        }
    }

    private async Task SyncOneAsync(string brand, bool createMissing, CancellationToken ct)
    {
        IReadOnlyList<UpgWogPrice> prices;
        try
        {
            prices = await _client.FetchAsync(brand, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately: no fallback write. Today's fetch failing must not be able to change a
            // price, and the prices we already have are the last ones the vendor told us.
            _logger.LogError(ex,
                "{Brand} price sync failed to fetch prices; keeping the last known pump prices", brand);
            return;
        }

        try
        {
            var result = await _import.HandleAsync(
                new ImportUpgWogPricesCommand(UpgWogPriceSheet.Write(prices), brand, DryRun: false, createMissing),
                Guid.Empty, SyncActorName, ct);

            if (result.Errors.Count > 0)
            {
                _logger.LogError(
                    "{Brand} price sync read {Count} error(s) from the sheet; nothing was written: {Errors}",
                    brand, result.Errors.Count,
                    string.Join(" | ", result.Errors.Select(e => e.Message)));
                return;
            }

            if (result.Created.Count > 0)
            {
                // Information, not Debug: the catalogue grew and nobody typed it. Each created fuel
                // also carries the vendor's own code, which is what makes the next tick match it.
                _logger.LogInformation(
                    "{Brand} sync added {Count} fuel(s) the catalogue did not have: {Fuels}.",
                    brand, result.Created.Count,
                    string.Join(", ", result.Created.Select(f => $"{f.Name} [{f.Code}]")));
            }

            if (result.Held.Count > 0)
            {
                // Information, not Debug: the catalogue already had this grade under a name of its
                // own, so the price went to the row that was already there and that row was given
                // the brand's code. The operator is told which row it was - one grade is one row,
                // and a second row for it would be a second price on the shelf.
                _logger.LogInformation(
                    "{Brand} publishes {Count} grade(s) we already sell under our own name: {Grades}. "
                    + "The price went to the row that already existed, and it was given the brand's code.",
                    brand, result.Held.Count,
                    string.Join(" | ", result.Held.Select(g => $"{g.Code} = \"{g.OurFuelName}\"")));
            }

            // Repriced lists every fuel the sheet matched, changed or not - "the sheet says 92.90 and
            // so do we" is not news every fifteen minutes. Only a move is.
            var moved = result.Repriced.Where(f => f.Changed).ToList();
            if (moved.Count > 0)
            {
                // Information, not Debug, and it has to stay that way: a price move is the one thing
                // this job exists to do, and production logs at Information. Measured on 2026-10-27,
                // UPG's gas and three WOG grades had moved since the window opened and left no line
                // anywhere - OKKO and KLO log the same event at Information, so the only search that
                // answered "when did this move?" was fuel_price_history.
                _logger.LogInformation(
                    "{Brand} sync repriced {Count} of {Matched} fuel(s): {Fuels}.",
                    brand, moved.Count, result.Repriced.Count,
                    string.Join(", ", moved.Select(f =>
                        $"{f.Name} {f.PreviousPumpPerLiter?.ToString("F2") ?? "none"} → {f.PumpPerLiter:F2}")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "{Brand} price sync fetched {Count} price(s) but failed to apply them",
                brand, prices.Count);
        }
    }
}