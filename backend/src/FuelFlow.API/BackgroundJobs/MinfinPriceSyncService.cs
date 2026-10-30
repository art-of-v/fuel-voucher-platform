using FuelFlow.Features.Pricing.ImportMinfinPrices;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.BackgroundJobs;

/// <summary>
/// Keeps the KLO and WOG catalogues priced from Minfin's fuel price table.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="OkkoPriceSyncService"/> and <see cref="UpgPriceSyncService"/>, for
/// the networks whose own prices are not readable from this host: KLO publishes none at all, and
/// WOG's page answers this address with an empty <c>204</c> (2026-10-29). Minfin publishes the
/// average retail price of every network, both included, and <see cref="MinfinNetwork"/> is what
/// says which of our fuels each column prices.
/// </para>
/// <para>
/// <b>Two clocks.</b> What a network charges is read every fifteen minutes; whether it sells a grade
/// we do not carry is a question for a weekly job, because creating a fuel is a decision and a new
/// grade is news on a scale of weeks.
/// </para>
/// <para>
/// <b>A failed fetch changes nothing.</b> A table that is down, or that has changed shape, leaves the
/// prices as they are - in particular it does not price a fuel at zero and does not archive a grade
/// it could not see. One network failing does not stop the other: they are separate rows of the same
/// page, but a row that cannot be read says nothing about the row beside it.
/// </para>
/// </remarks>
public sealed class MinfinPriceSyncService
{
    /// <summary>Actor recorded on audit rows written by the scheduler — there is no person behind them.</summary>
    private const string SyncActorName = "minfin-price-sync";

    private readonly IMinfinPriceClient _client;
    private readonly ImportMinfinPricesCommandHandler _import;
    private readonly ApplicationDbContext _context;
    private readonly MinfinPriceOptions _options;
    private readonly ILogger<MinfinPriceSyncService> _logger;

    public MinfinPriceSyncService(
        IMinfinPriceClient client,
        ImportMinfinPricesCommandHandler import,
        ApplicationDbContext context,
        IOptions<MinfinPriceOptions> options,
        ILogger<MinfinPriceSyncService> logger)
    {
        _client = client;
        _import = import;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// The fifteen-minute refresh: what the networks charge, for the grades we already carry.
    /// </summary>
    public Task SyncPricesAsync(CancellationToken cancellationToken = default)
        => SyncAllAsync(createMissing: false, cancellationToken);

    /// <summary>
    /// The weekly check: whether a network sells a grade we do not carry, and then the price of it.
    /// </summary>
    public Task SyncCatalogueAndPricesAsync(CancellationToken cancellationToken = default)
        => SyncAllAsync(createMissing: true, cancellationToken);

    private async Task SyncAllAsync(bool createMissing, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "Minfin price sync skipped (MinfinPrice:Enabled=false); the catalogue keeps its current pump prices");
            return;
        }

        foreach (var network in MinfinNetwork.All)
        {
            await SyncOneAsync(network, createMissing, ct);
        }
    }

    private async Task SyncOneAsync(MinfinNetwork network, bool createMissing, CancellationToken ct)
    {
        var name = network.Slug.ToUpperInvariant();

        IReadOnlyList<MinfinFuelPrice> sheet;
        try
        {
            sheet = await _client.FetchAsync(network.Slug, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately: no fallback write. Today's fetch failing must not be able to change a price.
            _logger.LogError(ex,
                "{Network} price sync failed to fetch prices from Minfin; keeping the last known pump prices",
                name);
            return;
        }

        try
        {
            var result = await _import.HandleAsync(
                new ImportMinfinPricesCommand(network.Slug, sheet, DryRun: false, createMissing),
                Guid.Empty, SyncActorName, ct);

            if (result.Created.Count > 0)
            {
                _logger.LogInformation(
                    "{Network} price sync added {Count} grade(s) the catalogue did not have: {Fuels}. "
                    + "Each is priced at what Minfin lists and waits for an operator to set a margin.",
                    name, result.Created.Count,
                    string.Join(", ", result.Created.Select(f => $"{f.Name} [{f.PumpPricePerLiter:F2}]")));
            }

            var changed = result.Repriced.Where(r => r.Changed).ToList();
            if (changed.Count > 0)
            {
                // Information, not Debug: production logs at Information, and a price move is the
                // one thing this job exists to do.
                _logger.LogInformation(
                    "{Network} price sync repriced {Count} grade(s): {Fuels}",
                    name, changed.Count,
                    string.Join(", ", changed.Select(r => $"{r.Name} {r.PreviousPumpPricePerLiter:F2}→{r.PumpPricePerLiter:F2}")));
            }

            if (result.GradesNotMapped.Count > 0)
            {
                // Minfin lists a grade this network's vocabulary has no counterpart for. Nothing is
                // written for it, so the fuel keeps its last price - which is why this is a warning:
                // the table moved and the catalogue could not follow.
                _logger.LogWarning(
                    "{Network} price sync found {Count} grade(s) Minfin lists and the catalogue cannot name: {Grades}. "
                    + "Nothing was written for them; MinfinNetwork needs the correspondence.",
                    name, result.GradesNotMapped.Count,
                    string.Join(", ", result.GradesNotMapped));
            }

            if (createMissing && result.GradesNotListed.Count > 0)
            {
                _logger.LogInformation(
                    "{Network} catalogue sync found {Count} grade(s) Minfin does not list: {Grades}. "
                    + "Their price stays where it was; Minfin averages five grades, not a vendor's whole shelf.",
                    name, result.GradesNotListed.Count,
                    string.Join(", ", result.GradesNotListed));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "{Network} price sync fetched {Count} price(s) but failed to apply them",
                name, sheet.Count);
        }
    }
}
