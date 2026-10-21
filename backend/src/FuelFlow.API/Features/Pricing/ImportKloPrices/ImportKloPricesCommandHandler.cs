using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ScrapeMinfinPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportKloPrices;

/// <param name="CreateMissing">
/// Whether a grade the catalogue lacks becomes a row. True for the weekly catalogue check, false
/// for the fifteen-minute price refresh: a new grade is news that arrives on a scale of weeks, and
/// a price refresh that created rows would add a fuel to the catalogue every time the table grew -
/// which is a decision, and it belongs to a job that runs weekly.
/// </param>
public sealed record ImportKloPricesCommand(
    IReadOnlyList<MinfinFuelPrice> Sheet,
    bool DryRun,
    bool CreateMissing = true);

/// <summary>A grade the catalogue did not have and now has, or would have in a dry run.</summary>
public sealed record KloPriceFuelCreated(Guid FuelTypeId, string Name, decimal PumpPricePerLiter);

/// <summary>A grade that was repriced: what it cost before, and what it costs now.</summary>
public sealed record KloPriceFuelRepriced(Guid FuelTypeId, string Name, string Grade, decimal PumpPricePerLiter, decimal? PreviousPumpPricePerLiter, bool Changed);

public sealed record KloPriceImportResult(
    IReadOnlyList<KloPriceFuelCreated> Created,
    IReadOnlyList<KloPriceFuelRepriced> Repriced,
    IReadOnlyList<string> BelowCostSkipped,
    IReadOnlyList<string> GradesNotSold,
    bool DryRun);

/// <summary>
/// Brings the KLO catalogue up to Minfin's prices for KLO.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a third party's table.</b> KLO publishes no prices: their products page lists the grades
/// they sell and the "more" link on every card is dead. Minfin publishes the average retail price of
/// every network, KLO included, and its five grades - "А 95+", "А 95", "А 92", "ДП", "Газ" - are
/// the five our KLO catalogue already holds, because an operator naming a fuel after the column is
/// how the catalogue got those names in the first place.
/// </para>
/// <para>
/// <b>Matched by name, not by code.</b> The KLO fuels carry no brand code, and Minfin publishes a
/// grade name rather than a code, so name is what there is to match on. The comparison ignores
/// whitespace and case: our fuel is "А95" and the column is "А 95", and they are the same grade.
/// A match that finds a fuel with no code GIVES it one - the column's name - because a code is what
/// makes a fuel resolve exactly, and this is the only source that can say which grade it is.
/// </para>
/// <para>
/// <b>What it writes.</b> The pump price, which is what the vendor charges. The cost, the margin and
/// the minimum discount are ours and are never touched: Minfin knows what the driver pays and
/// nothing about what we pay.
/// </para>
/// <para>
/// <b>What it refuses.</b> A pump price that would sell the fuel below our own cost is refused, the
/// same guard the other vendors' imports carry - a price move by a third party must not be able to
/// take a fuel off sale unattended.
/// </para>
/// </remarks>
public sealed class ImportKloPricesCommandHandler
{
    /// <summary>The brand this importer serves.</summary>
    public const string KloSlug = "klo";

    /// <summary>
    /// The sizes a new grade is sold in: the same three the UPG/WOG importer gives a grade it
    /// creates, so a KLO fuel and a WOG one behave alike on the shelf.
    /// </summary>
    private static readonly int[] DefaultNominals = [10, 20, 50];

    private readonly ApplicationDbContext _context;
    private readonly PriceHistoryRecorder _priceRecorder;
    private readonly BrandSlugResolver _brands;

    public ImportKloPricesCommandHandler(
        ApplicationDbContext context,
        PriceHistoryRecorder priceRecorder,
        BrandSlugResolver brands)
    {
        _context = context;
        _priceRecorder = priceRecorder;
        _brands = brands;
    }

    public async Task<KloPriceImportResult> HandleAsync(
        ImportKloPricesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        var brandId = await _brands.ResolveAsync(KloSlug, ct);
        if (brandId is null)
        {
            throw new InvalidOperationException(
                $"No live brand carries the slug '{KloSlug}', so the KLO prices cannot be imported.");
        }

        if (command.Sheet.Count == 0)
        {
            throw new InvalidOperationException(
                "The KLO price sheet carried no rows. Refusing to apply it: an empty sheet is a page "
                + "that failed to load, not a network that stopped selling fuel.");
        }

        var klo = brandId.Value;
        var now = DateTime.UtcNow;
        var actor = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;

        var ours = await _context.FuelTypes
            .AsTracking()
            .Where(f => f.BrandId == klo && !f.IsDeleted)
            .ToListAsync(ct);

        // One key for both sides: the grade's name, upper-cased, with the whitespace gone. Minfin
        // writes "А 95" and an operator wrote "А95", and they are the same grade.
        var byName = new Dictionary<string, FuelTypeEntity>(StringComparer.Ordinal);
        foreach (var fuel in ours)
        {
            byName[Key(fuel.Name)] = fuel;
        }

        var created = new List<KloPriceFuelCreated>();
        var repriced = new List<KloPriceFuelRepriced>();
        var belowCost = new List<string>();
        var notSold = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in command.Sheet)
        {
            var key = Key(row.Grade);
            seen.Add(key);

            if (!byName.TryGetValue(key, out var fuel))
            {
                if (!command.CreateMissing)
                {
                    // A grade we do not carry, on a refresh that is only allowed to move prices.
                    // Skipped rather than created; the weekly catalogue check is what brings it in.
                    continue;
                }

                var made = await CreateAsync(klo, row, actor, now, command.DryRun, ct);
                created.Add(made);
                continue;
            }

            var previousPump = fuel.PumpPricePerLiter;
            var changed = previousPump != row.PricePerLiter;

            var cost = fuel.SupplierPricePerLiter ?? 0m;
            var profit = fuel.MarginUahPerLiter ?? 0m;
            var minDiscount = fuel.MinDiscountPerLiter ?? 0m;

            if (!fuel.AllowBelowCost && FuelPricing.IsBelowCost(cost, profit, row.PricePerLiter))
            {
                belowCost.Add(fuel.Name);
                continue;
            }

            if (!command.DryRun)
            {
                fuel.PumpPricePerLiter = row.PricePerLiter;
                // A fuel with no code cannot resolve by code, and this is the only source that can
                // say which grade it is. The code is the column's name, which is the same string the
                // operator would have typed.
                if (string.IsNullOrWhiteSpace(fuel.BrandFuelCode))
                {
                    fuel.BrandFuelCode = row.Grade;
                }

                fuel.UpdatedAtUtc = now;
            }

            repriced.Add(new KloPriceFuelRepriced(
                fuel.Id, fuel.Name, row.Grade, row.PricePerLiter, previousPump, changed));
        }

        // The grades our catalogue holds that Minfin does not list for KLO. Reported, not archived:
        // a network's own grade can be one Minfin does not average, and archiving a fuel over a
        // third party's table would be a decision this importer has no standing to make.
        foreach (var fuel in ours.Where(f => !seen.Contains(Key(f.Name))))
        {
            notSold.Add(fuel.Name);
        }

        if (!command.DryRun)
        {
            await _context.SaveChangesAsync(ct);

            foreach (var fuel in ours.Where(f => repriced.Any(r => r.FuelTypeId == f.Id)))
            {
                await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.PumpImport, actor, now, ct);
            }
        }

        return new KloPriceImportResult(created, repriced, belowCost, notSold, command.DryRun);
    }

    /// <summary>
    /// Creates one grade the catalogue lacks, with the price Minfin lists it at.
    /// </summary>
    private async Task<KloPriceFuelCreated> CreateAsync(
        Guid brandId,
        MinfinFuelPrice row,
        Guid? actor,
        DateTime now,
        bool dryRun,
        CancellationToken ct)
    {
        if (!dryRun)
        {
            var fuel = new FuelTypeEntity
            {
                Id = Guid.CreateVersion7(),
                BrandId = brandId,
                // The grade's name, as the column states it: that is how an operator names it too.
                Name = row.Grade,
                BrandFuelCode = row.Grade,
                // Priced at the pump on purpose. We know what the driver pays and nothing about what
                // we pay, so the pump is the only figure we can honestly show; an operator sets the
                // margin and it becomes a real price.
                PumpPricePerLiter = row.PricePerLiter,
                SupplierPricePerLiter = null,
                MarginUahPerLiter = null,
                AllowBelowCost = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelTypes.Add(fuel);

            foreach (var liters in DefaultNominals)
            {
                _context.FuelNominals.Add(new FuelNominal
                {
                    FuelTypeId = fuel.Id,
                    Liters = liters,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.PumpImport, actor, now, ct);
        }

            return new KloPriceFuelCreated(Guid.Empty, row.Grade, row.PricePerLiter);
    }

    /// <summary>
    /// The key a grade is matched by: its name, upper-cased, with the whitespace gone.
    /// </summary>
    private static string Key(string name)
        => string.Concat(name.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
