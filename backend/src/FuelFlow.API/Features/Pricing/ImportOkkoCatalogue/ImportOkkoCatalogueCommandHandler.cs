using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportOkkoCatalogue;

public sealed record ImportOkkoCatalogueCommand(IReadOnlyList<OkkoPumpPrice> Sheet, bool DryRun);

/// <summary>One fuel the catalogue did not have and now has, or would have in a dry run.</summary>
public sealed record OkkoCatalogueCreated(string FuelTypeId, string Name, string BrandFuelCode, decimal PumpPricePerLiter);

public sealed record OkkoCatalogueApplyResult(
    int Created,
    int AlreadyKnown,
    IReadOnlyList<OkkoCatalogueCreated> CreatedFuels,
    /// <summary>Site rows the catalogue has no counterpart for and no category we sell (jet fuel, AdBlue).</summary>
    IReadOnlyList<string> UnclassifiedSiteFuels,
    /// <summary>Site rows we know the grade of but could not name, because the payload carried no label.</summary>
    IReadOnlyList<string> UnnamedSiteFuels,
    bool DryRun);

/// <summary>
/// Brings the OKKO catalogue up to what OKKO sells, from the same price sheet the pump sync already
/// fetches every fifteen minutes. Planning #349, step 4.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it writes, and what it refuses to.</b> A missing fuel becomes a row: the brand's code, the
/// brand's own label, and an offer carrying nothing but the published pump price. Everything an
/// operator owns is left alone — our price, the margin, the minimum discount, the below-cost opt-in —
/// and an existing fuel is not touched at all, because the pump sync already owns its price. The
/// asymmetry is the point: this is a way of learning that a grade exists, not a way of pricing one.
/// </para>
/// <para>
/// <b>It never archives.</b> "Not on the site this morning" is not "remove it from the catalogue": a
/// broken selector, an anti-bot page or a locale change would empty the storefront of every fuel at
/// once, and every voucher already sold points at a fuel that must still resolve. A grade the brand
/// drops stays here until an operator archives it, which is the same policy as everywhere else in the
/// catalog (#217).
/// </para>
/// <para>
/// <b>It only creates grades we already sell.</b> A row whose code maps to no canonical category —
/// aviation jet, the AdBlue additive — is reported and dropped. Inventing a fuel the catalogue has no
/// category for is how an import turns a supplier's new product into a sellable row nobody priced.
/// </para>
/// <para>
/// <b>A created fuel is not sellable.</b> It gets an offer with a pump price and <em>no volumes</em>,
/// and the storefront lists volumes: so the fuel is invisible until an operator opens the brand
/// screen and sets a margin, at which point the default nominals are seeded and it appears at a price
/// a human chose. An import that guessed a margin would be publishing a price nobody approved, which
/// is the incident the whole offer split came from (F-33: 1,00 грн/л on the live site).
/// </para>
/// <para>
/// <b>An empty sheet is refused outright.</b> Zero rows must never be able to mean "the catalogue is
/// now empty", so the handler throws before writing anything rather than treating it as nothing to do.
/// </para>
/// </remarks>
public sealed class ImportOkkoCatalogueCommandHandler
{
    private const string OkkoStationId = "okko";

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;
    private readonly OfferPriceRecorder _priceRecorder;

    public ImportOkkoCatalogueCommandHandler(
        ApplicationDbContext context,
        AuditEventService events,
        OfferPriceRecorder priceRecorder)
    {
        _context = context;
        _events = events;
        _priceRecorder = priceRecorder;
    }

    public async Task<OkkoCatalogueApplyResult> ApplyAsync(
        ImportOkkoCatalogueCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        if (command.Sheet.Count == 0)
        {
            throw new InvalidOperationException(
                "The OKKO sheet carried no rows. Refusing to apply it: an empty sheet is a fetch that "
                + "failed to read the page, not a catalogue that should be emptied.");
        }

        var now = DateTime.UtcNow;
        var created = new List<OkkoCatalogueCreated>();
        var alreadyKnown = 0;
        var unclassified = new List<string>();
        var unnamed = new List<string>();

        // What we already carry, live rows only: an archived fuel must not stop a fresh one from being
        // created for a grade the brand sells again.
        //
        // BOTH the codes and the names, and the names are what made this necessary. Production carried
        // fifteen fuels and not one code, so a code-only test said "every grade is missing" and the
        // import tried to create "ГАЗ" beside the "ГАЗ" that was already there — refused by
        // ux_fuel_types_station_name, every fifteen minutes, since the deploy. A name is not an identity
        // (that is the whole point of the code), but on its own it is enough to say "we sell this grade
        // already", and the code it lacks is the backfill's job, not this import's.
        var carried = (await _context.FuelTypes
                .AsNoTracking()
                .Where(f => f.StationId == OkkoStationId && !f.IsDeleted)
                .ToListAsync(ct));

        var knownCodes = carried
            .Where(f => f.BrandFuelCode is not null)
            .Select(f => f.BrandFuelCode!)
            .ToHashSet(StringComparer.Ordinal);

        var knownNames = carried
            .Select(f => FuelTypeEntity.NormaliseBrandFuelCode(f.Name))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in command.Sheet)
        {
            var code = FuelTypeEntity.NormaliseBrandFuelCode(row.SiteFuelCode);
            if (code is null) continue;

            if (OkkoFuelClassifier.CategoryFromSiteFuelCode(code) is null)
            {
                unclassified.Add(code);
                continue;
            }

            var name = FuelTypeEntity.NormaliseBrandFuelCode(row.FuelName) is { } rawName && rawName.Length > 0
                ? rawName
                : null;

            // The code first: it is the stronger claim, and a fuel holding it needs nothing from us.
            // The name second, which is how a grade we sell under our own spelling is left alone.
            if (knownCodes.Contains(code) || (name is not null && knownNames.Contains(name)))
            {
                alreadyKnown++;
                continue;
            }

            if (name is null)
            {
                // Known grade, no label. Creating it would mean naming a customer-facing fuel after its
                // code — "DP" on the storefront — so it is reported instead.
                unnamed.Add(code);
                continue;
            }

            knownCodes.Add(code);
            knownNames.Add(name);

            if (command.DryRun)
            {
                created.Add(new OkkoCatalogueCreated("(dry run)", name, code, row.PricePerLiter));
                continue;
            }

            var fuel = new FuelTypeEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                StationId = OkkoStationId,
                Name = name,
                BrandFuelCode = code,
                // Zero, not the pump: the headline is the customer's price, and there is none until an
                // operator sets a margin. The pump sync owns BasePrice/DiscountPrice from here on.
                BasePrice = 0m,
                DiscountPrice = 0m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelTypes.Add(fuel);

            var offer = new FuelOffer
            {
                Id = OfferKeys.For(fuel.Id, DataKinds.Real),
                StationId = OkkoStationId,
                FuelTypeId = fuel.Id,
                DataKind = DataKinds.Real,
                // The one supplier-owned figure on the offer. Cost, margin and final stay null, and no
                // volumes exist yet, so nothing is sellable and nothing is displayed.
                PumpPricePerLiter = row.PricePerLiter,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelOffers.Add(offer);

            // The price the offer was born with, so a fuel's history starts at its creation instead of at
            // the first time somebody repriced it. A row of mostly nulls is the honest record here: the
            // catalogue knows the pump and nothing else.
            await _priceRecorder.RecordAsync(offer, OfferPriceReasons.CatalogueImport, actingUserId, now, ct);

            created.Add(new OkkoCatalogueCreated(fuel.Id, name, code, row.PricePerLiter));
        }

        if (!command.DryRun && created.Count > 0)
        {
            await _context.SaveChangesAsync(ct);

            // One audit row per created fuel, so a catalogue that grew while nobody was looking says so
            // in the same place a typed one does.
            foreach (var c in created)
            {
                await _events.RecordEventAsync(
                    "Fuel", c.FuelTypeId, "FuelCreatedByImport",
                    null,
                    JsonSerializer.Serialize(new { c.Name, c.BrandFuelCode, c.PumpPricePerLiter }),
                    actingUserId == Guid.Empty ? Guid.Empty : actingUserId,
                    actingUserName,
                    $"{c.Name} ({c.BrandFuelCode}): added from OKKO's published list at {c.PumpPricePerLiter:F2} UAH/L pump, "
                    + "unpriced and not on sale until a margin is set",
                    OkkoStationId,
                    ct);
            }
        }

        return new OkkoCatalogueApplyResult(
            Created: created.Count,
            AlreadyKnown: alreadyKnown,
            CreatedFuels: created,
            UnclassifiedSiteFuels: unclassified.Distinct(StringComparer.Ordinal).ToList(),
            UnnamedSiteFuels: unnamed.Distinct(StringComparer.Ordinal).ToList(),
            DryRun: command.DryRun);
    }
}
