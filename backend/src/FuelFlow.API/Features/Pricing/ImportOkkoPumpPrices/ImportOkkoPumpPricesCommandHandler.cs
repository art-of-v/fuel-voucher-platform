using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportOkkoPumpPrices;

/// <summary>Imports OKKO's published pump prices (колонка) into the OKKO catalog.</summary>
public sealed record ImportOkkoPumpPricesCommand(string Content, bool DryRun);

/// <summary>
/// Imports a price list fetched straight from OKKO's API, skipping the CSV round-trip.
/// <para>
/// The same write path as the uploaded-sheet import — the admin endpoint hands a CSV to
/// <see cref="ImportOkkoPumpPricesCommandHandler"/> for a one-off, while the recurring
/// <c>OkkoPriceSyncService</c> job calls this. Both funnel into
/// <see cref="ApplyAsync"/>, so there is exactly one implementation of "what a pump price does to the
/// catalog" and the manual and automatic paths cannot drift.
/// </para>
/// </summary>
/// <param name="Prices">Fuel/price pairs as published by OKKO, keyed by site fuel code.</param>
/// <param name="ActingUserId">Empty for the scheduled job — nothing was typed by a person.</param>
public sealed record SyncOkkoPumpPricesCommand(
    IReadOnlyList<OkkoPumpPrice> Prices, bool DryRun);

/// <summary>
/// Writes a parsed OKKO price sheet into the catalog's pump field.
/// </summary>
/// <para>
/// <b>How a fuel is matched to a row of the sheet.</b> By <see cref="FuelTypeEntity.BrandFuelCode"/>
/// first — the brand publishes that code on the sheet it also publishes prices on, so a fuel whose
/// catalogue carries it is matched by equality and needs no interpretation. A fuel with no code is
/// matched the way this always was, by collapsing both sides onto a canonical category
/// (<see cref="OkkoFuelClassifier"/>), which is why the seeded fuels still price while they are empty.
/// A fuel that has a code the sheet does not carry is reported as having no price and is
/// <em>not</em> re-matched by category: two fuels of one category are exactly what the code exists to
/// tell apart, and falling back would hand the price to whichever sorted first.
/// </para>
/// <para>
/// Writing the pump means writing the offer's ceiling and repricing its volumes, mirroring the pump
/// path of <c>BrandsController.UpdateFuel</c>. A pump change is NOT a cost event, so the supplier cost
/// (<c>SupplierPricePerLiter</c> — what we pay the reseller) is held constant; only the ceiling moves.
/// It lowers the sale price only when the ceiling actually binds
/// (<c>final = min(cost + profit, pump − minDiscount)</c>).
/// </para>
/// <para>
/// The supplier cost is per-package rather than per-fuel, and packages of the same fuel can in
/// principle disagree, so the cost of the <i>first</i> package (lowest id order, matching the
/// operator path's <c>packages.First()</c>) is the one the price is computed from. This mirrors
/// <c>UpdateFuel</c>, which likewise computes a single per-liter price for the whole fuel from one
/// representative package.
/// </para>
/// <para>
/// Packages load <c>AsTracking</c> because the API DbContext is read-only by default for queries —
/// without it the mutations below would silently no-op on save.
/// </para>
/// </summary>
public sealed class ImportOkkoPumpPricesCommandHandler
{
    /// <summary>See <see cref="BackfillBrandFuelCodesCommandHandler"/> for why this is a slug.</summary>
    private const string OkkoSlug = "okko";

    private readonly ApplicationDbContext _context;
    private readonly BrandSlugResolver _brands;
    private readonly AuditEventService _events;

    // Appends to fuel_price_history for every fuel this import repriced. Injected rather than reached
    // for, so the unattended path is as bound to leave history as the operator path is - this is the
    // one write path that runs every quarter hour with nobody watching.
    private readonly PriceHistoryRecorder _priceRecorder;

    public ImportOkkoPumpPricesCommandHandler(
        ApplicationDbContext context,
        BrandSlugResolver brands,
        AuditEventService events,
        PriceHistoryRecorder priceRecorder)
    {
        _context = context;
        _brands = brands;
        _events = events;
        _priceRecorder = priceRecorder;
    }

    public async Task<OkkoPumpApplyResult> HandleAsync(
        ImportOkkoPumpPricesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        var parsed = OkkoPriceSheetParser.Parse(command.Content);
        var prices = parsed.Rows.Select(r => new OkkoPumpPrice(r.FuelCode, r.PricePerLiter)).ToList();
        var result = await ApplyAsync(prices, command.DryRun, actingUserId, actingUserName, ct);
        return result with { Errors = parsed.Errors };
    }

    /// <summary>
    /// Applies a fetched price list to the catalog. Shared by the CSV upload endpoint and the
    /// recurring sync job.
    /// </summary>
    public async Task<OkkoPumpApplyResult> ApplyAsync(
        IReadOnlyList<OkkoPumpPrice> prices,
        bool dryRun,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        // Two lookups, and which one answers depends on whether the catalogue carries the brand's own
        // code for a fuel. The code is the one to trust: the sheet publishes it, so matching it needs no
        // guess. The category map is what this did before any fuel had a code - site code collapses to a
        // category, a name collapses to the same category, and two fuels of one category are then
        // indistinguishable - so it stays as the fallback for the fuels that have no code yet, and stops
        // being consulted the moment one does.
        //
        // Resolved before anything else, because "OKKO" is a slug and the rows are keyed by id now
        // (planning D-21). A brand that is not in the catalogue means there is nothing to price, and
        // saying so loudly matters: a sync that reported "0 priced" every quarter hour would be
        // indistinguishable from a working sync with a quiet price sheet.
        var okkoId = await _brands.ResolveAsync(OkkoSlug, ct);
        if (okkoId is null)
        {
            // Reported as an error rather than returned quietly: a sync that reported "0 priced"
            // every quarter hour would be indistinguishable from a working sync with a quiet price
            // sheet, and the pump price is what the public site sells against.
            return new OkkoPumpApplyResult(
                FuelsMatched: 0,
                FuelsChanged: 0,
                PackagesRepriced: 0,
                Applied: [],
                UnmatchedSiteFuels: [],
                OkkoFuelsWithoutPrice: [],
                BelowCostSkipped: [],
                Errors: [new OkkoPriceSheetError(0,
                    $"No live brand carries the slug '{OkkoSlug}', so no pump price could be applied.")],
                DryRun: dryRun);
        }

        var priceByCode = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in prices)
        {
            var code = FuelTypeEntity.NormaliseBrandFuelCode(row.SiteFuelCode);
            if (code is not null) priceByCode.TryAdd(code, row.PricePerLiter);
        }

        var classified = prices
            .Select(p => (Price: p, Category: OkkoFuelClassifier.CategoryFromSiteFuelCode(p.SiteFuelCode)))
            .ToList();

        var priceByCategory = new Dictionary<OkkoFuelCategory, decimal>();
        foreach (var (row, category) in classified)
        {
            if (category is { } c) priceByCategory.TryAdd(c, row.PricePerLiter);
        }

        // Ordered in memory, not in SQL: the ordinal comparer that makes "first package" deterministic
        // is not translatable by EF, and the row count here is a handful of fuel types either way.
        var okkoFuelTypes = (await _context.FuelTypes
                .AsTracking()
                .Where(ft => ft.BrandId == okkoId.Value)
                .ToListAsync(ct))
            .OrderBy(ft => ft.Id, StringComparer.Ordinal)
            .ToList();

// A pump price is a property of WHAT IS SOLD, so it lands on the fuel, which is the row every
        // reader already looks at. A nominal carries no stored price, so a pump change does not
        // rewrite anything per size - it moves the fuel's rate and every size of both pools follows.
        // The counts the report gives back are the fuel's nominal count across the pools.
        var now = DateTime.UtcNow;
        var nominalCountByFuel = await _context.FuelNominals
            .AsNoTracking()
            .GroupBy(n => n.FuelTypeId)
            .Select(g => new { FuelTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FuelTypeId, x => x.Count, ct);

        var actor = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;

        var applied = new List<OkkoPumpApplied>();
        var withoutPrice = new List<string>();
        var belowCostSkipped = new List<string>();
        var matchedCategories = new HashSet<OkkoFuelCategory>();
        var matchedCodes = new HashSet<string>(StringComparer.Ordinal);
        var changedCount = 0;
        var packagesRepriced = 0;

        // The fuels this run touched, for the one history write at the end. Collected rather than
        // recorded inside the loop, which would cost a query per fuel on a path that runs unattended
        // over the whole catalogue every quarter hour.
        var repricedFuels = new List<FuelTypeEntity>();

        foreach (var ft in okkoFuelTypes)
        {
            // By the brand's code where the catalogue has one, by category otherwise.
            var catalogueCode = FuelTypeEntity.NormaliseBrandFuelCode(ft.BrandFuelCode);
            OkkoFuelCategory? category;
            decimal pump;
            string siteCode;

            if (catalogueCode is not null)
            {
                // A code that the sheet does not carry means this fuel has no pump price today. It is
                // deliberately NOT re-matched by category: two fuels of one category are exactly what a
                // code disambiguates, and guessing would hand the price to whichever one sorted first.
                if (!priceByCode.TryGetValue(catalogueCode, out pump))
                {
                    withoutPrice.Add(ft.Name);
                    continue;
                }

                siteCode = catalogueCode;
                category = OkkoFuelClassifier.CategoryFromSiteFuelCode(catalogueCode);
                matchedCodes.Add(catalogueCode);
            }
            else
            {
                category = OkkoFuelClassifier.CategoryFromName(ft.Name);
                if (category is null || !priceByCategory.TryGetValue(category.Value, out pump))
                {
                    withoutPrice.Add(ft.Name);
                    continue;
                }

                siteCode = classified.First(c => c.Category == category).Price.SiteFuelCode;
            }

            var vols = nominalCountByFuel.TryGetValue(ft.Id, out var vcount) ? vcount : 0;

            // Cost, margin and discount are the fuel's, so both pools move together by
            // construction rather than by a loop that happens to keep them in step.
            var cost = ft.SupplierPricePerLiter ?? 0m; // held constant: a pump change is not a cost event
            var profit = ft.MarginUahPerLiter ?? 0m;
            var minDiscount = ft.MinDiscountPerLiter ?? 0m;
            var finalPerLiter = FuelPricing.FinalPerLiter(cost, profit, pump, minDiscount);

            // A pump low enough that the ceiling (pump - minDiscount) drops under our own cost would
            // make every sale of this fuel below cost - and checkout then refuses to sell it at all
            // (BelowCostSaleBlockedException), so the fuel becomes unbuyable until a human intervenes.
            // The manual path refuses the same write unless the fuel is opted in via AllowBelowCost
            // (BrandsController.UpdateFuel); the automatic path must behave identically, or a
            // third party's price move could take a fuel off sale unattended.
            if (!ft.AllowBelowCost && FuelPricing.IsBelowCost(cost, profit, pump, minDiscount))
            {
                belowCostSkipped.Add(ft.Name);
                continue;
            }

            if (category is { } matched) matchedCategories.Add(matched);

            var previousPump = ft.PumpPricePerLiter;
            var changed = previousPump != pump;

            if (!dryRun)
            {
                ft.PumpPricePerLiter = pump;

                // Keep the fuel-type base/discount headline in step, as the operator write path does.
                ft.BasePrice = Math.Round(pump, 2, MidpointRounding.AwayFromZero);
                ft.DiscountPrice = Math.Round(finalPerLiter, 2, MidpointRounding.AwayFromZero);
                ft.UpdatedAtUtc = now;

                // Nothing per nominal is restated, and that is the change: a nominal's price is the
                // fuel's rate × its litres, so this loop used to write the same number into every row
                // every quarter hour - and erased any per-nominal override the moment a pump moved. The
                // fuel is the single price now, which is what `PackagesRepriced` counts and what the
                // operator screen shows.
                repricedFuels.Add(ft);
            }

            if (changed) changedCount++;
            packagesRepriced += vols;
            applied.Add(new OkkoPumpApplied(
                ft.Id, ft.Name, siteCode, pump, finalPerLiter, previousPump, changed, vols));
        }

            // The "a fuel matched but has nothing to price" row is gone with the offer it belonged to.
        // There is no state in which a catalogue fuel has no cost row to write the pump into, so
        // the branch could only ever fire on data that no longer exists.
        if (!dryRun && changedCount > 0)
        {
            // One row per fuel whose price actually moved, all stamped with this run's `now` so the
            // run reads as one moment rather than twenty-five - and all appended in one call, so the
            // recorder fetches the existing history in a single query instead of one per fuel. It
            // also re-stamps price_updated_at / price_updated_by_user_id.
            await _priceRecorder.RecordRangeAsync(
                repricedFuels, PriceHistoryReasons.PumpImport, actor, now, ct);
            await _context.SaveChangesAsync(ct);
            await RecordEventsAsync(applied, actor, actingUserName, ct);
        }

        // A sheet row is accounted for when either lookup consumed it: the code it publishes, or the
        // category a code-less fuel was matched by. What is left is genuinely unmatched and is what the
        // operator needs to see — a new grade on the site, or a grade this catalogue does not sell.
        var unmatchedSheet = prices
            .Select(p => FuelTypeEntity.NormaliseBrandFuelCode(p.SiteFuelCode))
            .OfType<string>()
            .Where(code => !matchedCodes.Contains(code)
                && !(OkkoFuelClassifier.CategoryFromSiteFuelCode(code) is { } cat
                     && matchedCategories.Contains(cat)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new OkkoPumpApplyResult(
            FuelsMatched: applied.Count,
            FuelsChanged: changedCount,
            PackagesRepriced: packagesRepriced,
            Applied: applied,
            UnmatchedSiteFuels: unmatchedSheet,
            OkkoFuelsWithoutPrice: withoutPrice,
            BelowCostSkipped: belowCostSkipped,
            Errors: Array.Empty<OkkoPriceSheetError>(),
            DryRun: dryRun);
    }

    /// <summary>
    /// Writes one <c>PumpPriceImported</c> audit row per fuel whose pump actually moved, mirroring the
    /// operator path's <c>PriceChanged</c> trail — so a price drop on the public site shows up in the
    /// brand history with the import, not just with whoever last typed a number.
    /// </summary>
    private async Task RecordEventsAsync(
        IReadOnlyList<OkkoPumpApplied> applied,
        Guid? actorId,
        string? actorName,
        CancellationToken ct)
    {
        foreach (var a in applied.Where(a => a.Changed))
        {
            var oldValue = a.PreviousPumpPricePerLiter is { } prev
                ? JsonSerializer.Serialize(new { a.FuelName, PumpPricePerLiter = prev })
                : null;
            var newValue = JsonSerializer.Serialize(new
            {
                a.FuelName,
                a.SiteFuelCode,
                PumpPricePerLiter = a.PumpPricePerLiter,
                a.FinalPricePerLiter,
            });
            var summary = a.PreviousPumpPricePerLiter is { } previous
                ? $"OKKO / {a.FuelName}: pump {previous:F2} → {a.PumpPricePerLiter:F2} UAH/L (imported from site)"
                : $"OKKO / {a.FuelName}: pump set to {a.PumpPricePerLiter:F2} UAH/L (imported from site)";

            await _events.RecordEventAsync(
                "Fuel", a.FuelTypeId, "PumpPriceImported",
                oldValue, newValue, actorId ?? Guid.Empty, actorName,
                summary, OkkoSlug, ct);
        }
    }
}
