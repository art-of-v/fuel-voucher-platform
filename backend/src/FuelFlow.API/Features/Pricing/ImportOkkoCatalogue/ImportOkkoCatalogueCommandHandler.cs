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

/// <summary>
/// A grade the sheet publishes and we already sell, under a name of our own choosing. Nothing is written
/// for it — a second row for a grade we already carry is the duplicate this whole identity rule exists to
/// prevent — and the pair is reported so an operator can give that fuel the brand's code.
/// </summary>
public sealed record OkkoCatalogueGradeHeld(string SiteFuelCode, OkkoFuelCategory Grade, string OurFuelName);

/// <summary>Two grades wanting one name; reported rather than resolved by renaming something by guesswork.</summary>
public sealed record OkkoCatalogueNameCollision(string Name, string SiteFuelCode);

public sealed record OkkoCatalogueApplyResult(
    int Created,
    int AlreadyKnown,
    IReadOnlyList<OkkoCatalogueCreated> CreatedFuels,
    /// <summary>Grades the sheet publishes that we already sell under our own name, with nothing written.</summary>
    IReadOnlyList<OkkoCatalogueGradeHeld> GradesHeld,
    /// <summary>Grades whose canonical name is already taken by a fuel of another grade; a human decides.</summary>
    IReadOnlyList<OkkoCatalogueNameCollision> NameCollisions,
    /// <summary>Site rows the catalogue has no counterpart for and no category we sell (jet fuel, AdBlue).</summary>
    IReadOnlyList<string> UnclassifiedSiteFuels,
    bool DryRun);

/// <summary>
/// Brings the OKKO catalogue up to what OKKO sells, from the same price sheet the pump sync already
/// fetches every fifteen minutes. Planning #349 step 4, #360.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row per grade.</b> A fuel is identified by (brand, grade), and the grade is read from the
/// brand's own code — never from the label the site renders, because those are per-brand, per-locale
/// renderings that move with a designer ("95 PULLS", "ДП ЄВРО", "100 PULLS"). A sheet row whose grade
/// the catalogue already holds is <em>reported</em>, never created: that is what stopped the import
/// adding "ДП" beside the "ДП PULLS" that already meant the same diesel, and "95" beside "100 PULLS".
/// Two rows for one grade is not a cosmetic problem — the storefront lists the result as two fuels, and
/// a voucher importer resolving by grade could price against either twin.
/// </para>
/// <para>
/// <b>What it writes, and what it refuses to.</b> A missing grade becomes a row: the brand's code, a name
/// derived from the grade, and an offer carrying nothing but the published pump price. Everything an
/// operator owns is left alone — our price, the margin, the minimum discount, the below-cost opt-in,
/// the name itself — and an existing fuel is not touched at all, because the pump sync already owns its
/// price. The asymmetry is the point: this is a way of learning that a grade exists, not a way of
/// pricing one.
/// </para>
/// <para>
/// <b>It never renames.</b> "А95 ЄВРО" stays "А95 ЄВРО": the operator put "ЄВРО" there and it means
/// something to whoever reads it. A rename is an event in the price history and a human's decision, not
/// something an import does to a row nobody is looking at.
/// </para>
/// <para>
/// <b>It never archives.</b> "Not on the site this morning" is not "remove it from the catalogue": a
/// broken selector, an anti-bot page or a locale change would empty the storefront of every fuel at
/// once, and every voucher already sold points at a fuel that must still resolve. A grade the brand
/// drops stays here — and, having no nominals of its own, leaves the shelf on its own (#217, #360).
/// </para>
/// <para>
/// <b>It only creates grades we sell.</b> A row whose code maps to no canonical grade — aviation jet,
/// the AdBlue additive — is reported and dropped. Inventing a fuel the catalogue has no grade for is how
/// an import turns a supplier's new product into a sellable row nobody priced.
/// </para>
/// <para>
/// <b>A created fuel is not sellable.</b> It gets an offer with a pump price and <em>no volumes</em>,
/// and the storefront lists volumes: so the fuel is invisible until an operator adds nominals, at which
/// point it appears at the pump price with no acquisition cost behind it. An import that guessed a
/// margin would be publishing a price nobody approved, which is the incident the whole offer split came
/// from (F-33: 1,00 грн/л on the live site).
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
        var gradesHeld = new List<OkkoCatalogueGradeHeld>();
        var nameCollisions = new List<OkkoCatalogueNameCollision>();

        // What we already carry, live rows only: an archived fuel must not stop a fresh one from being
        // created for a grade the brand sells again.
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

        // The grade each carried fuel already occupies, and under what name. This is what makes a second
        // row for one grade impossible: a fuel is identified by (brand, grade), and the grade is read
        // from the brand's code rather than from spelling.
        //
        // Code first, name second, because our own catalogue holds both spellings - "ДП" and "DP",
        // "А-95" and "А95 ЄВРО" - and each pair is one fuel. A fuel whose code is unmapped still holds
        // its grade if its name classifies.
        var gradeHolder = new Dictionary<OkkoFuelCategory, string>();
        foreach (var fuel in carried)
        {
            var held = OkkoFuelClassifier.CategoryFromSiteFuelCode(fuel.BrandFuelCode)
                ?? OkkoFuelClassifier.CategoryFromName(fuel.Name);

            if (held is { } grade && !gradeHolder.ContainsKey(grade))
                gradeHolder[grade] = fuel.Name;
        }

        foreach (var row in command.Sheet)
        {
            var code = FuelTypeEntity.NormaliseBrandFuelCode(row.SiteFuelCode);
            if (code is null) continue;

            if (OkkoFuelClassifier.CategoryFromSiteFuelCode(code) is not { } grade)
            {
                unclassified.Add(code);
                continue;
            }

            // Code first: a live fuel already holding the brand's own code for this grade is the whole job
            // done, and there is nothing for an operator to act on.
            if (knownCodes.Contains(code))
            {
                alreadyKnown++;
                continue;
            }

            // The grade is the identity. If we sell this grade under another name - ours, or the site's,
            // or a spelling we have never seen - then a second row is a duplicate by construction.
            // Production grew one by skipping this test: nothing carried a code, so every grade looked
            // missing and "ДП" was added beside the "ДП PULLS" that already meant the same diesel.
            //
            // Reported rather than created, and reported loudly, because this is the one case where a
            // human still has work to do: our fuel needs the brand's code to resolve exactly.
            if (gradeHolder.TryGetValue(grade, out var holder))
            {
                gradesHeld.Add(new OkkoCatalogueGradeHeld(code, grade, holder));
                continue;
            }

            // The name comes from the grade, never from the site's label. The labels are per-brand
            // renderings of a grade - "95 PULLS", "ДП ЄВРО", "100 PULLS" - that move with a designer or
            // a locale, and taking one as the name is how a fuel called "95" ended up next to
            // "100 PULLS".
            var name = OkkoFuelClassifier.CanonicalName(grade);

            if (!command.DryRun && knownNames.Contains(name))
            {
                // Two grades wanting one name. The unique index would refuse it and guessing which to
                // rename would be worse, so it is reported and a human decides.
                nameCollisions.Add(new OkkoCatalogueNameCollision(name, code));
                continue;
            }

            knownCodes.Add(code);
            knownNames.Add(name);
            gradeHolder[grade] = name;

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
            GradesHeld: gradesHeld
                .DistinctBy(h => (h.Grade, h.OurFuelName))
                .ToList(),
            NameCollisions: nameCollisions
                .DistinctBy(c => (c.Name, c.SiteFuelCode))
                .ToList(),
            UnclassifiedSiteFuels: unclassified.Distinct(StringComparer.Ordinal).ToList(),
            DryRun: command.DryRun);
    }
}
