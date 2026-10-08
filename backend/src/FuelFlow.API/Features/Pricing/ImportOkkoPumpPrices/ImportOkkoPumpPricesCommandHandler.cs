using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportOkkoPumpPrices;

/// <summary>Imports OKKO's published pump prices (колонка) into the OKKO catalog.</summary>
public sealed record ImportOkkoPumpPricesCommand(string Content, bool DryRun);

/// <summary>
/// Writes a parsed OKKO price sheet into the catalog's pump field. For each OKKO fuel type whose
/// canonical category (<see cref="OkkoFuelClassifier"/>) matches a scraped row, it writes the
/// sheet's price into <see cref="FuelPackage.PumpPricePerLiter"/> on every package and reprices
/// <c>FinalPricePerLiter</c>/<c>OriginalPrice</c> plus the fuel-type headline — exactly mirroring
/// the pump path of <c>ProvidersController.UpdateFuel</c>.
/// <para>
/// A pump change is NOT a cost event, so the supplier cost (<c>SupplierPricePerLiter</c>) is held
/// constant — only the ceiling moves. Writing the pump turns on the struck "до" price and the
/// per-liter savings display; it only lowers the sale price if the ceiling actually binds
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
    private const string OkkoStationId = "okko";

    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _events;

    public ImportOkkoPumpPricesCommandHandler(ApplicationDbContext context, ProviderEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<OkkoPumpApplyResult> HandleAsync(
        ImportOkkoPumpPricesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        var parsed = OkkoPriceSheetParser.Parse(command.Content);

        // Collapse the sheet onto canonical categories; first price per category wins. Rows whose code
        // does not classify, or whose category we do not carry, are reported back as unmatched.
        var classified = parsed.Rows
            .Select(r => (Row: r, Category: OkkoFuelClassifier.CategoryFromSiteFuelCode(r.FuelCode)))
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
                .Where(ft => ft.StationId == OkkoStationId)
                .ToListAsync(ct))
            .OrderBy(ft => ft.Id, StringComparer.Ordinal)
            .ToList();

        var okkoFuelTypeIds = okkoFuelTypes.Select(ft => ft.Id).ToList();
        var packagesByFuel = (await _context.FuelPackages
                .AsTracking()
                .Where(p => okkoFuelTypeIds.Contains(p.FuelTypeId))
                .ToListAsync(ct))
            .GroupBy(p => p.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id, StringComparer.Ordinal).ToList());

        var now = DateTime.UtcNow;
        var actor = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;

        var applied = new List<OkkoPumpApplied>();
        var withoutPrice = new List<string>();
        var matchedCategories = new HashSet<OkkoFuelCategory>();
        var changedCount = 0;
        var packagesRepriced = 0;

        foreach (var ft in okkoFuelTypes)
        {
            var category = OkkoFuelClassifier.CategoryFromName(ft.Name);
            if (category is null || !priceByCategory.TryGetValue(category.Value, out var pump))
            {
                withoutPrice.Add(ft.Name);
                continue;
            }

            matchedCategories.Add(category.Value);
            var pkgs = packagesByFuel.TryGetValue(ft.Id, out var found) ? found : new List<FuelPackage>();
            var siteCode = classified.First(c => c.Category == category).Row.FuelCode;

            var representative = pkgs.FirstOrDefault();
            var cost = representative?.SupplierPricePerLiter ?? 0m; // held constant: a pump change is not a cost event
            var profit = representative?.MarginUahPerLiter ?? 0m;
            var minDiscount = representative?.MinDiscountPerLiter ?? 0m;
            var finalPerLiter = FuelPricing.FinalPerLiter(cost, profit, pump, minDiscount);

            var previousPump = pkgs.Select(p => p.PumpPricePerLiter).FirstOrDefault(v => v is not null);
            // Same fuel type in the catalog may hold packages with different pump values if they were
            // edited out of band; report the first and rewrite them all, so the receipt is never a lie.
            var changed = previousPump != pump;

            foreach (var pkg in pkgs)
            {
                var price = Math.Round(finalPerLiter * pkg.Liters, 2, MidpointRounding.AwayFromZero);
                if (!command.DryRun)
                {
                    pkg.PumpPricePerLiter = pump;
                    pkg.FinalPricePerLiter = finalPerLiter;
                    pkg.Price = price;
                    pkg.OriginalPrice = FuelPricing.OriginalPackagePrice(pump, pkg.Liters, price);
                    pkg.UpdatedAtUtc = now;
                    pkg.PriceUpdatedAt = now;
                    pkg.PriceUpdatedByUserId = actor;
                }
            }

            // Keep the fuel-type base/discount headline in step, as the operator write path does.
            if (!command.DryRun && pkgs.Count > 0)
            {
                ft.BasePrice = Math.Round(pump, 2, MidpointRounding.AwayFromZero);
                ft.DiscountPrice = Math.Round(finalPerLiter, 2, MidpointRounding.AwayFromZero);
                ft.UpdatedAtUtc = now;
            }

            if (changed) changedCount++;
            packagesRepriced += pkgs.Count;
            applied.Add(new OkkoPumpApplied(
                ft.Id, ft.Name, siteCode, pump, finalPerLiter, previousPump, changed, pkgs.Count));
        }

        if (!command.DryRun && changedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
            await RecordEventsAsync(applied, actor, actingUserName, ct);
        }

        var unmatchedSheet = classified
            .Where(c => c.Category is null || !matchedCategories.Contains(c.Category.Value))
            .Select(c => c.Row.FuelCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new OkkoPumpApplyResult(
            FuelsMatched: applied.Count,
            FuelsChanged: changedCount,
            PackagesRepriced: packagesRepriced,
            Applied: applied,
            UnmatchedSiteFuels: unmatchedSheet,
            OkkoFuelsWithoutPrice: withoutPrice,
            Errors: parsed.Errors,
            DryRun: command.DryRun);
    }

    /// <summary>
    /// Writes one <c>PumpPriceImported</c> audit row per fuel whose pump actually moved, mirroring the
    /// operator path's <c>PriceChanged</c> trail — so a price drop on the public site shows up in the
    /// provider history with the import, not just with whoever last typed a number.
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
                summary, OkkoStationId, ct);
        }
    }
}