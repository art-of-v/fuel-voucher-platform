using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
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

public sealed record OkkoCatalogueArchived(string FuelTypeId, string Name, int LiveVouchers, bool Archived);

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
    /// <summary>Fuels OKKO no longer publishes: archived when their stock is finished, reported when it is not.</summary>
    IReadOnlyList<OkkoCatalogueArchived> ArchivedFuels,
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
    /// <summary>See <see cref="BackfillBrandFuelCodesCommandHandler"/> for why this is a slug.</summary>
    private const string OkkoSlug = "okko";

    private readonly ApplicationDbContext _context;
    private readonly BrandSlugResolver _brands;
    private readonly AuditEventService _events;
    private readonly OfferPriceRecorder _priceRecorder;

    public ImportOkkoCatalogueCommandHandler(
        ApplicationDbContext context,
        BrandSlugResolver brands,
        AuditEventService events,
        OfferPriceRecorder priceRecorder)
    {
        _context = context;
        _brands = brands;
        _events = events;
        _priceRecorder = priceRecorder;
    }

    public async Task<OkkoCatalogueApplyResult> ApplyAsync(
        ImportOkkoCatalogueCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        // Thrown, not returned: this handler already refuses an empty sheet outright rather than
        // treating it as nothing to do, and a catalogue that cannot be written because the network it
        // names is not in ours belongs in the same class of mistake. Reporting "0 created" would let
        // the fifteen-minute schedule look like a working one.
        var okkoId = await _brands.ResolveAsync(OkkoSlug, ct);
        if (okkoId is null)
            throw new InvalidOperationException(
                $"No live brand carries the slug '{OkkoSlug}', so the catalogue cannot be imported.");

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
        // Which of our fuels the sheet still publishes. A fuel missing from this set is a candidate
        // for archiving - subject to the voucher check, which is what actually decides.
        var seenFuelIds = new HashSet<string>(StringComparer.Ordinal);
        var nameCollisions = new List<OkkoCatalogueNameCollision>();

        // What we already carry, live rows only: an archived fuel must not stop a fresh one from being
        // created for a grade the brand sells again.
        var carried = (await _context.FuelTypes
                .AsNoTracking()
                .Where(f => f.BrandId == okkoId.Value && !f.IsDeleted)
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
var gradeHolder = new Dictionary<OkkoFuelCategory, (string Id, string Name)>();
        // Every fuel holding a grade, not only the one that "represents" it. One row per grade is the
        // rule, but until the duplicates are gone a second row must not be archived as "dropped by
        // the brand" while the brand is plainly still publishing that grade.
        var byGrade = new Dictionary<OkkoFuelCategory, List<FuelTypeEntity>>();
        foreach (var fuel in carried)
        {
            var held = OkkoFuelClassifier.CategoryFromSiteFuelCode(fuel.BrandFuelCode)
                ?? OkkoFuelClassifier.CategoryFromName(fuel.Name);

            if (held is not { } grade) continue;

            if (!gradeHolder.ContainsKey(grade))
                gradeHolder[grade] = (fuel.Id, fuel.Name);

            if (!byGrade.TryGetValue(grade, out var sameGrade))
                byGrade[grade] = sameGrade = [];
            sameGrade.Add(fuel);
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
                // Seen, so not a candidate for archiving at the end of this run. `First` rather than a
                // lookup because the live (Brand, code) index already guarantees there is one - and
                // the whole grade group with it, since the brand is plainly still publishing that grade.
                seenFuelIds.Add(carried.First(f => f.BrandFuelCode == code).Id);
                if (byGrade.TryGetValue(grade, out var sameGrade))
                    foreach (var sibling in sameGrade) seenFuelIds.Add(sibling.Id);

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
                if (byGrade.TryGetValue(grade, out var sameGrade))
                    foreach (var sibling in sameGrade) seenFuelIds.Add(sibling.Id);
                else
                    seenFuelIds.Add(holder.Id);

                gradesHeld.Add(new OkkoCatalogueGradeHeld(code, grade, holder.Name));
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
            // The id is recorded below, once the fuel exists: a dry run has no row to point at.

            if (command.DryRun)
            {
                created.Add(new OkkoCatalogueCreated("(dry run)", name, code, row.PricePerLiter));
                continue;
            }

            var fuel = new FuelTypeEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                BrandId = okkoId.Value,
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
            // The brand publishes it - we just learned that from this very sheet - so it must not be a
            // candidate for archiving at the end of this same run.
            seenFuelIds.Add(fuel.Id);

            var offer = new FuelOffer
            {
                Id = OfferKeys.For(fuel.Id, DataKinds.Real),
                BrandId = okkoId.Value,
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
                    OkkoSlug,
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
            ArchivedFuels: await ArchiveDroppedFuelsAsync(okkoId.Value, seenFuelIds, actingUserId, actingUserName, command.DryRun, ct),
            DryRun: command.DryRun);
    }

    /// <summary>
    /// Archives the fuels OKKO no longer publishes, and only those whose stock is finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to archive nothing at all, on the reasoning that "not on the site this morning" is not
    /// "remove it from the catalogue" — a broken selector, an anti-bot page or a locale change would
    /// empty the storefront of every fuel at once. That reasoning is kept, and made safe by the second
    /// condition: a fuel can only go when there is nothing left to sell.
    /// </para>
    /// <para>
    /// <b>Both conditions, and the operator's rule for them</b> (2026-10-17): the brand must no longer
    /// publish the grade, and every one of its vouchers must be spent or expired. The sheet proves the
    /// first; the voucher table proves the second. Neither alone is enough — absence from the sheet
    /// says nothing about stock we paid for, and an idle fuel the brand still sells is simply one we
    /// happen not to carry.
    /// </para>
/// <para>
    /// A fuel whose vouchers are still live is <b>reported, not archived</b>, and keeps trading against
    /// stock we have already paid for. That is the safe failure: it stays on the shelf until the
    /// vouchers are used up, and the next tick — fifteen minutes later — tries again.
    /// </para>
    /// <para>
    /// <b>Only fuels that carry the brand's own code are candidates.</b> Absence from the sheet proves
    /// something about a grade the brand publishes; it proves nothing about a row an operator typed,
    /// which is our own naming and may not correspond to anything the brand sells. Retiring those
    /// automatically would remove fuels nobody asked to remove — and the catalogue is full of them
    /// precisely because operators name fuels in their own words. They are archived by hand, and the
    /// admin endpoint enforces the same voucher check there.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<OkkoCatalogueArchived>> ArchiveDroppedFuelsAsync(
        Guid brandId,
        IReadOnlySet<string> seenFuelIds,
        Guid actingUserId,
        string? actingUserName,
        bool dryRun,
        CancellationToken ct)
    {
        var blocking = VoucherStatuses.Blocking;

        var absent = await _context.FuelTypes
            .IgnoreQueryFilters()
            .Where(f => f.BrandId == brandId
                && !f.IsDeleted
                && f.BrandFuelCode != null
                && !seenFuelIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);

        if (absent.Count == 0)
            return [];

        var dropped = new List<OkkoCatalogueArchived>();

        foreach (var fuel in absent)
        {
            var live = await _context.FuelVouchers
                .IgnoreQueryFilters()
                .CountAsync(v => v.FuelTypeId == fuel.Id && !v.IsDeleted && blocking.Contains(v.Status), ct);

            if (live > 0)
            {
                // Reported so the reason is visible rather than inferred from a fuel that quietly
                // stopped being imported.
                dropped.Add(new OkkoCatalogueArchived(fuel.Id, fuel.Name, live, Archived: false));
                continue;
            }

            if (!dryRun)
            {
                var fuelRow = await _context.FuelTypes.IgnoreQueryFilters()
                    .FirstAsync(f => f.Id == fuel.Id, ct);
                fuelRow.IsDeleted = true;
                fuelRow.DeletedAtUtc = DateTime.UtcNow;
                fuelRow.UpdatedAtUtc = DateTime.UtcNow;

                // Tracked writes rather than ExecuteUpdateAsync: the latter is a relational-only
                // operator, and this handler is unit-tested on the in-memory provider where it throws
                // at translation time. One fuel per fifteen minutes is not a volume problem.
                var now = DateTime.UtcNow;
                var offers = await _context.FuelOffers.IgnoreQueryFilters()
                    .Where(o => o.FuelTypeId == fuel.Id && !o.IsDeleted)
                    .ToListAsync(ct);
                var offerIds = offers.Select(o => o.Id).ToList();


                foreach (var offer in offers)
                {
                    offer.IsDeleted = true;
                    offer.DeletedAtUtc = now;
                    offer.UpdatedAtUtc = now;
                }

                await _context.SaveChangesAsync(ct);

                await _events.RecordEventAsync(
                    "Fuel", fuel.Id, "FuelArchivedByImport",
                    null,
                    JsonSerializer.Serialize(new { fuel.Name, Reason = "no longer published by OKKO" }),
                    actingUserId == Guid.Empty ? Guid.Empty : actingUserId,
                    actingUserName,
                    $"{fuel.Name}: archived — OKKO no longer publishes this grade and it has no live vouchers",
                    OkkoSlug,
                    ct);
            }

            dropped.Add(new OkkoCatalogueArchived(fuel.Id, fuel.Name, 0, Archived: !dryRun));
        }

        return dropped;
    }
}
