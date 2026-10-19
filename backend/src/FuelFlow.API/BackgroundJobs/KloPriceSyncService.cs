using FuelFlow.Features.Pricing.ImportKloPrices;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.BackgroundJobs;

/// <summary>
/// Keeps the KLO catalogue priced from Minfin's fuel price table.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="OkkoPriceSyncService"/> and <see cref="UpgWogPriceSyncService"/>,
/// for the one network that publishes no prices of its own. Minfin publishes the average retail price
/// of every network, KLO included, and its five grades are the five our KLO catalogue holds.
/// </para>
/// <para>
/// <b>Two clocks.</b> What a network charges is read every fifteen minutes; whether it sells a grade
/// we do not carry is a question for a weekly job, because creating a fuel is a decision and a new
/// grade is news on a scale of weeks.
/// </para>
/// <para>
/// <b>A failed fetch changes nothing.</b> A table that is down, or that has changed shape, leaves the
/// prices as they are - in particular it does not price a fuel at zero and does not archive a grade
/// it could not see.
/// </para>
/// </remarks>
public sealed class KloPriceSyncService
{
    /// <summary>Actor recorded on audit rows written by the scheduler — there is no person behind them.</summary>
    private const string SyncActorName = "klo-price-sync";

    private readonly IMinfinPriceClient _client;
    private readonly ImportKloPricesCommandHandler _import;
    private readonly ApplicationDbContext _context;
    private readonly MinfinPriceOptions _options;
    private readonly ILogger<KloPriceSyncService> _logger;

    public KloPriceSyncService(
        IMinfinPriceClient client,
        ImportKloPricesCommandHandler import,
        ApplicationDbContext context,
        IOptions<MinfinPriceOptions> options,
        ILogger<KloPriceSyncService> logger)
    {
        _client = client;
        _import = import;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The fifteen-minute refresh: what the network charges, for the grades we already carry.
    /// </summary>
    public async Task SyncPricesAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled())
        {
            return;
        }

        var sheet = await FetchAsync(cancellationToken);
        if (sheet is null)
        {
            return;
        }

        await ApplyAsync(sheet, createMissing: false, cancellationToken);
    }

    /// <summary>
    /// The weekly check: whether KLO sells a grade we do not carry, and then the price of it.
    /// </summary>
    public async Task SyncCatalogueAndPricesAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled())
        {
            return;
        }

        var sheet = await FetchAsync(cancellationToken);
        if (sheet is null)
        {
            return;
        }

        await ApplyAsync(sheet, createMissing: true, cancellationToken);
    }

    private bool Enabled()
    {
        if (_options.Enabled)
        {
            return true;
        }

        _logger.LogDebug(
            "KLO price sync skipped (MinfinPrice:Enabled=false); the catalogue keeps its current pump prices");
        return false;
    }

    private async Task<IReadOnlyList<MinfinFuelPrice>?> FetchAsync(CancellationToken ct)
    {
        try
        {
            return await _client.FetchAsync(ImportKloPricesCommandHandler.KloSlug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately: no fallback write. Today's fetch failing must not be able to change a price.
            _logger.LogError(ex,
                "KLO price sync failed to fetch prices; keeping the last known pump prices");
            return null;
        }
    }

    private async Task ApplyAsync(
        IReadOnlyList<MinfinFuelPrice> sheet,
        bool createMissing,
        CancellationToken ct)
    {
        try
        {
            var result = await _import.HandleAsync(
                new ImportKloPricesCommand(sheet, DryRun: false, createMissing),
                Guid.Empty, SyncActorName, ct);

            if (result.Created.Count > 0)
            {
                _logger.LogInformation(
                    "KLO price sync added {Count} grade(s) the catalogue did not have: {Fuels}. "
                    + "Each is priced at what Minfin lists and waits for an operator to set a margin.",
                    result.Created.Count,
                    string.Join(", ", result.Created.Select(f => $"{f.Name} [{f.PumpPricePerLiter:F2}]")));
            }

            var changed = result.Repriced.Where(r => r.Changed).ToList();
            if (changed.Count > 0)
            {
                _logger.LogInformation(
                    "KLO price sync repriced {Count} grade(s): {Fuels}",
                    changed.Count,
                    string.Join(", ", changed.Select(r => $"{r.Name} {r.PreviousPumpPricePerLiter:F2}→{r.PumpPricePerLiter:F2}")));
            }

            foreach (var skipped in result.BelowCostSkipped)
            {
                // Warning, not Error: refusing a below-cost price is the guard doing its job, and
                // this runs every fifteen minutes. At Error it filled the admin error journal with
                // the same refusal until a real failure could not be found in it.
                _logger.LogWarning(
                    "KLO price sync refused {Grade}: the price Minfin lists would sell it below our cost.",
                    skipped);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "KLO price sync fetched {Count} price(s) but failed to apply them",
                sheet.Count);
        }
    }
}
