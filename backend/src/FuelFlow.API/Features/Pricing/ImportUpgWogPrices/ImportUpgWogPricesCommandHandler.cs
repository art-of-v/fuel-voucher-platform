using System.Globalization;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportUpgWogPrices;

/// <summary>Imports the pump prices UPG and WOG publish, and the grades they publish that we do not yet sell.</summary>
public sealed record ImportUpgWogPricesCommand(string Content, string BrandId, bool DryRun);

/// <summary>One grade created by the import. Reported so the operator learns what appeared.</summary>
public sealed record UpgWogFuelCreated(string BrandId, string Name, string Code, decimal PumpPerLiter);

/// <summary>One existing grade repriced. <paramref name="Changed"/> is false when the pump already matched.</summary>
public sealed record UpgWogFuelRepriced(
    string FuelTypeId, string Name, string Code, decimal PumpPerLiter,
    decimal? PreviousPumpPerLiter, bool Changed, int VolumesRepriced);

public sealed record UpgWogPriceImportResult(
    IReadOnlyList<UpgWogFuelCreated> Created,
    IReadOnlyList<UpgWogFuelRepriced> Repriced,
    IReadOnlyList<string> BelowCostSkipped,
    IReadOnlyList<OkkoPriceSheetError> Errors,
    bool DryRun)
{
    /// <summary>Fuels matched by their code plus the grades newly created - everything this run touched.</summary>
    public int Touched => Repriced.Count + Created.Count;
}

/// <summary>
/// Writes a UPG or WOG price sheet into that brand's catalogue.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists when OKKO already has two.</b> OKKO publishes a JSON API with both prices and
/// labels, so it gets a catalogue importer and a price importer and a fifteen-minute job. UPG and WOG
/// publish the same price blob inside their HTML, and their fetchers emit the same
/// <c>fuelCode,pricePerLiter</c> CSV - the contract {@link OkkoPriceSheetParser} already owns - but
/// nothing consumed it. The sheet was readable and the catalogue was hand-typed anyway.
/// </para>
/// <para>
/// <b>Why one import does both jobs.</b> For OKKO those are separate because a site code is a slug
/// (<c>upg95</c>) and naming a fuel from it is a judgement, so catalogue creation needs its own
/// classifier. Here the published token <i>is</i> the name the brand shows its own customers: UPG
/// prints <c>upg95</c> and <c>EURO DIESEL</c>, WOG prints <c>ДП Mustang+</c>. There is nothing to
/// translate, so a grade we do not sell is created under the brand's own words rather than held back
/// for a human to rename.
/// </para>
/// <para>
/// <b>What it never writes.</b> The name and <see cref="FuelTypeEntity.MarginUahPerLiter"/> of a fuel
/// that already exists. The margin is the operator's commercial term and a third party's price move
/// must not reprice the business; the name is how a human recognises the fuel, and renaming a row
/// because a site changed its label would silently re-key the catalogue.
/// </para>
/// <para>
/// <b>A new grade is born priced at the pump.</b> We know what the brand charges and nothing about
/// what we pay, so there is no cost and no margin. It goes on the shelf at the pump price and stays
/// there until an operator sets a margin - which is what planning #360 calls for, and why it is
/// deliberately not priced at zero, which is a different and much worse claim.
/// </para>
/// </remarks>
public sealed class ImportUpgWogPricesCommandHandler
{
    /// <summary>The brands this importer serves, and the only ones its sheet may name.</summary>
    public static readonly string[] SupportedBrandIds = ["upg", "wog"];

    private const int DefaultMinDiscountPerLiter = 0;
    private static readonly int[] DefaultNominals = [10, 20, 50];

    private readonly ApplicationDbContext _context;
    private readonly OfferPriceRecorder _priceRecorder;

    public ImportUpgWogPricesCommandHandler(
        ApplicationDbContext context,
        OfferPriceRecorder priceRecorder)
    {
        _context = context;
        _priceRecorder = priceRecorder;
    }

    public async Task<UpgWogPriceImportResult> HandleAsync(
        ImportUpgWogPricesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        var brandId = command.BrandId.Trim().ToLowerInvariant();
        if (!SupportedBrandIds.Contains(brandId))
        {
            return new UpgWogPriceImportResult(
                [], [], [],
                [new OkkoPriceSheetError(0, $"Unsupported brand '{command.BrandId}'")],
                command.DryRun);
        }

        var parsed = OkkoPriceSheetParser.Parse(command.Content);
        if (parsed.Errors.Count > 0)
        {
            return new UpgWogPriceImportResult([], [], [], parsed.Errors, command.DryRun);
        }

        // Matched on the code alone. Unlike OKKO there is no category fallback here and there must
        // not be one: these brands publish two distinct 95s and two distinct diesels, and a name- or
        // category-based fallback is exactly what would hand both prices to whichever row sorted first.
        var byCode = (await _context.FuelTypes
                .AsTracking()
                .Where(f => f.BrandId == brandId && !f.IsDeleted)
                .ToListAsync(ct))
            .Where(f => FuelTypeEntity.NormaliseBrandFuelCode(f.BrandFuelCode) is not null)
            .GroupBy(f => FuelTypeEntity.NormaliseBrandFuelCode(f.BrandFuelCode)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Id, StringComparer.Ordinal).First());

        var fuelIds = byCode.Values.Select(f => f.Id).ToList();
        var offersByFuel = (await _context.FuelOffers
                .AsTracking()
                .Where(o => fuelIds.Contains(o.FuelTypeId))
                .ToListAsync(ct))
            .GroupBy(o => o.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id, StringComparer.Ordinal).ToList());

        var now = DateTime.UtcNow;
        var actor = actingUserId == Guid.Empty ? (Guid?)null : actingUserId;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var created = new List<UpgWogFuelCreated>();
        var repriced = new List<UpgWogFuelRepriced>();
        var belowCost = new List<string>();

        foreach (var row in parsed.Rows)
        {
            var code = FuelTypeEntity.NormaliseBrandFuelCode(row.FuelCode);
            if (code is null) continue;
            if (!seen.Add(code)) continue;

            if (!byCode.TryGetValue(code, out var fuel))
            {
                var made = await CreateAsync(brandId, code, row.PricePerLiter, actor, now, command.DryRun, ct);
                created.Add(made);
                continue;
            }

            var offers = offersByFuel.TryGetValue(fuel.Id, out var found) ? found : [];
            var changedAny = false;
            var volumesRepriced = 0;

            foreach (var offer in offers)
            {
                var cost = offer.SupplierPricePerLiter ?? 0m;
                var profit = fuel.MarginUahPerLiter ?? 0m;
                var minDiscount = offer.MinDiscountPerLiter ?? DefaultMinDiscountPerLiter;

                // Identical guard to the OKKO automatic path: a third party's price move must not be
                // able to take a fuel below cost unattended, or the checkout guard refuses to sell it.
                if (!fuel.AllowBelowCost && FuelPricing.IsBelowCost(cost, profit, row.PricePerLiter, minDiscount))
                {
                    belowCost.Add(fuel.Name);
                    continue;
                }

                var previous = offer.PumpPricePerLiter;
                var changed = previous != row.PricePerLiter;

                if (!command.DryRun && changed)
                {
                    offer.PumpPricePerLiter = row.PricePerLiter;
                    offer.UpdatedAtUtc = now;
                }

                if (!command.DryRun)
                {
                    await _priceRecorder.RecordAsync(offer, OfferPriceReasons.PumpImport, actor, now, ct);
                }

                changedAny |= changed;
        // A nominal has no stored price, so a pump change rewrites nothing per size - it moves
        // the offer's rate and every size follows. The count the report gives back is the
        // fuel's nominal count, because the sizes belong to the fuel rather than to one
        // pool's offer.
        volumesRepriced += await _context.FuelNominals
            .CountAsync(n => n.FuelTypeId == offer.FuelTypeId, ct);
            }

            // The headline figures follow the offers, exactly as the operator write path does, and only
            // when something actually moved - an unchanged sheet must leave the row untouched, so a
            // second run over the same file writes nothing at all.
            if (!command.DryRun && changedAny)
            {
                var headline = offers.FirstOrDefault();
                var final = headline is null
                    ? FuelPricing.FinalPerLiter(0m, 0m, row.PricePerLiter, DefaultMinDiscountPerLiter)
                    : FuelPricing.FinalPerLiter(
                        headline.SupplierPricePerLiter ?? 0m,
                        fuel.MarginUahPerLiter ?? 0m,
                        row.PricePerLiter,
                        headline.MinDiscountPerLiter ?? DefaultMinDiscountPerLiter);

                fuel.BasePrice = Math.Round(row.PricePerLiter, 2, MidpointRounding.AwayFromZero);
                fuel.DiscountPrice = Math.Round(final, 2, MidpointRounding.AwayFromZero);
                fuel.UpdatedAtUtc = now;
            }

            repriced.Add(new UpgWogFuelRepriced(
                fuel.Id, fuel.Name, code, row.PricePerLiter,
                offers.FirstOrDefault()?.PumpPricePerLiter, changedAny, volumesRepriced));
        }

        if (!command.DryRun)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new UpgWogPriceImportResult(created, repriced, belowCost, [], command.DryRun);
    }

    /// <summary>
    /// Creates one grade the brand sells and we do not, with its offer and default nominals.
    /// </summary>
    /// <remarks>
    /// An offer is created with the fuel because a fuel with no offer has no cost to price from and
    /// would sit on the shelf at a derived zero - the very state planning #360 forbids. The offer
    /// carries the pump and no supplier cost, which is the honest description of what we know.
    /// </remarks>
    private async Task<UpgWogFuelCreated> CreateAsync(
        string brandId,
        string code,
        decimal pump,
        Guid? actor,
        DateTime now,
        bool dryRun,
        CancellationToken ct)
    {
        if (!dryRun)
        {
            var fuel = new FuelTypeEntity
            {
                Id = Guid.CreateVersion7().ToString(),
                BrandId = brandId,
                // The brand's own word for the grade, which is what it prints to its customers. Not
                // derived, not normalised: `ДП Mustang+` and `upg95` are both already the name.
                Name = code,
                BrandFuelCode = code,
                // Priced at the pump on purpose. We know what they charge and nothing about what we
                // pay, so the pump is the only figure we can honestly show; an operator sets the margin
                // and it becomes a real price. Zero here would claim we sell this fuel for nothing.
                BasePrice = Math.Round(pump, 2, MidpointRounding.AwayFromZero),
                DiscountPrice = Math.Round(pump, 2, MidpointRounding.AwayFromZero),
                MarginUahPerLiter = null,
                AllowBelowCost = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelTypes.Add(fuel);

            var offer = new FuelOffer
            {
                Id = OfferKeys.For(fuel.Id, DataKinds.Real),
                BrandId = brandId,
                FuelTypeId = fuel.Id,
                SupplierPricePerLiter = null,
                MarginPercent = null,
                PumpPricePerLiter = Math.Round(pump, 2, MidpointRounding.AwayFromZero),
                MinDiscountPerLiter = DefaultMinDiscountPerLiter,
                DataKind = DataKinds.Real,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelOffers.Add(offer);

            // History starts at the price it was born with, so the fuel's story begins at its creation
            // rather than at the first time an operator edited it.
            await _priceRecorder.RecordAsync(offer, OfferPriceReasons.PumpImport, actor, now, ct);

        foreach (var liters in DefaultNominals)
        {
            _context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = offer.FuelTypeId,
                Liters = liters,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
            await _context.SaveChangesAsync(ct);
        }

        return new UpgWogFuelCreated(brandId, code, code, Math.Round(pump, 2, MidpointRounding.AwayFromZero));
    }
}
