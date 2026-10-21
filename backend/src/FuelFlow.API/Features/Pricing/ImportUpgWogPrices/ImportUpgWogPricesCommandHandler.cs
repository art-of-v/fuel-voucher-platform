using System.Globalization;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Pricing.ImportUpgWogPrices;

/// <summary>Imports the pump prices UPG and WOG publish, and the grades they publish that we do not yet sell.</summary>
/// <param name="CreateMissing">
/// Whether a grade the brand sells and we do not become a row. True for the operator's own upload
/// and for the weekly catalogue check, false for the fifteen-minute price refresh: a new grade is
/// news that arrives on a scale of weeks, and a price refresh that created rows would add a fuel to
/// the catalogue every time a vendor added one - which is a decision, and it belongs to a job that
/// runs weekly rather than to the one that moves prices.
/// </param>
public sealed record ImportUpgWogPricesCommand(
    string Content,
    string BrandId,
    bool DryRun,
    bool CreateMissing = true);

/// <summary>One grade created by the import. Reported so the operator learns what appeared.</summary>
public sealed record UpgWogFuelCreated(string BrandId, string Name, string Code, decimal PumpPerLiter);

/// <summary>One existing grade repriced. <paramref name="Changed"/> is false when the pump already matched.</summary>
public sealed record UpgWogFuelRepriced(
    Guid FuelTypeId, string Name, string Code, decimal PumpPerLiter,
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
    private readonly PriceHistoryRecorder _priceRecorder;
    private readonly BrandSlugResolver _brands;

    public ImportUpgWogPricesCommandHandler(
        ApplicationDbContext context,
        PriceHistoryRecorder priceRecorder,
        BrandSlugResolver brands)
    {
        _context = context;
        _priceRecorder = priceRecorder;
        _brands = brands;
    }

    public async Task<UpgWogPriceImportResult> HandleAsync(
        ImportUpgWogPricesCommand command,
        Guid actingUserId,
        string? actingUserName,
        CancellationToken ct = default)
    {
        // The sheet names the network by its code, which is still how an operator and a supplier file
        // spell it; the row is keyed by a uuid since planning D-21. Resolved once here so the rest of
        // the import speaks the database's language.
        var brandSlug = command.BrandId.Trim().ToLowerInvariant();
        if (!SupportedBrandIds.Contains(brandSlug))
        {
            return new UpgWogPriceImportResult(
                [], [], [],
                [new OkkoPriceSheetError(0, $"Unsupported brand '{command.BrandId}'")],
                command.DryRun);
        }

        var brandId = await _brands.ResolveAsync(brandSlug, ct);
        if (brandId is null)
        {
            return new UpgWogPriceImportResult(
                [], [], [],
                [new OkkoPriceSheetError(0, $"Brand '{brandSlug}' is not in the catalogue")],
                command.DryRun);
        }

        var brandKey = brandId.Value;

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
                .Where(f => f.BrandId == brandKey && !f.IsDeleted)
                .ToListAsync(ct))
            .Where(f => FuelTypeEntity.NormaliseBrandFuelCode(f.BrandFuelCode) is not null)
            .GroupBy(f => FuelTypeEntity.NormaliseBrandFuelCode(f.BrandFuelCode)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Id).First());

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
                if (!command.CreateMissing)
                {
                    // A grade we do not carry, on a refresh that is only allowed to move prices.
                    // Skipped rather than created, and the weekly catalogue check is what brings it in.
                    continue;
                }

                var made = await CreateAsync(brandKey, code, row.PricePerLiter, actor, now, command.DryRun, ct);
                created.Add(made);
                continue;
            }

            var previousPump = fuel.PumpPricePerLiter;
            var changed = previousPump != row.PricePerLiter;
            var volumesRepriced = 0;

            // Identical guard to the OKKO automatic path: a third party's price move must not be
            // able to take a fuel below cost unattended, or the checkout guard refuses to sell it.
            var cost = fuel.SupplierPricePerLiter ?? 0m;
            var profit = fuel.MarginUahPerLiter ?? 0m;
            var minDiscount = fuel.MinDiscountPerLiter ?? DefaultMinDiscountPerLiter;

            if (!fuel.AllowBelowCost && FuelPricing.IsBelowCost(cost, profit))
            {
                belowCost.Add(fuel.Name);
                continue;
            }

            if (!command.DryRun && changed)
            {
                fuel.PumpPricePerLiter = row.PricePerLiter;
                fuel.UpdatedAtUtc = now;
            }

            if (!command.DryRun)
            {
                await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.PumpImport, actor, now, ct);
            }

            // A nominal has no stored price, so a pump change rewrites nothing per size - it moves the
            // fuel's rate and every size of both pools follows. The count the report gives back is the
            // fuel's nominal count, because the sizes belong to the fuel rather than to one pool.
            volumesRepriced = await _context.FuelNominals
                .CountAsync(n => n.FuelTypeId == fuel.Id, ct);

            // The headline figures follow the fuel, exactly as the operator write path does, and only
            // when something actually moved - an unchanged sheet must leave the row untouched, so a
            // second run over the same file writes nothing at all.
            if (!command.DryRun && changed)
            {
                // The customer price, or NULL when the fuel has no cost or no margin recorded.
                //
                // The nullable twin of the formula, and for the same reason the operator panel uses
                // it: a fuel nobody has priced has no price, and a price of ZERO is a price - it
                // says the customer takes the fuel for nothing, and the database refuses the row
                // (ck_fuel_types_prices_non_negative).
                //
                // This used to take the non-nullable formula with the cost and the margin defaulted
                // to zero, which for an uncosted fuel computed min(0 + 0, pump) = 0 and wrote it.
                // Fourteen fuels on production carried discount_price = 0.00 for exactly that
                // reason, and the moment the constraint landed this import would have failed on
                // every fuel whose pump had moved - which, at a cron of every fifteen minutes, is
                // up to ninety-six times a day.
                var customerPrice = FuelPricing.FinalPerLiterOrNull(
                    fuel.SupplierPricePerLiter, fuel.MarginUahPerLiter);

                // The price the customer pays is computed, so there is nothing to write for it: the
                // fuel carries the cost, the margin and the column price, and every reader derives
                // the customer price the same way. The column price is still written - it is the
                // reference the customer's saving is measured against.
                fuel.PumpPricePerLiter = row.PricePerLiter;
                fuel.UpdatedAtUtc = now;
            }

            repriced.Add(new UpgWogFuelRepriced(
                fuel.Id, fuel.Name, code, row.PricePerLiter, previousPump, changed, volumesRepriced));
        }

        if (!command.DryRun)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new UpgWogPriceImportResult(created, repriced, belowCost, [], command.DryRun);
    }

    /// <summary>
    /// Creates one grade the brand sells and we do not, with its price and default nominals.
    /// </summary>
    /// <remarks>
    /// The pump goes on the fuel, because it is what the brand charges and it is the only figure we
    /// know. It is the REFERENCE - the list price the customer's saving is measured against - and it
    /// is not the price the customer pays: we know nothing about what we pay, so nobody has priced
    /// this fuel yet, and the customer price is NULL until an operator sets the cost and the margin.
    /// Zero here would claim we sell this fuel for nothing, and the pump would claim we sell it at
    /// the pump, which is not true either.
    /// </remarks>
    private async Task<UpgWogFuelCreated> CreateAsync(
        Guid brandId,
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
                Id = Guid.CreateVersion7(),
                BrandId = brandId,
                // The brand's own word for the grade, which is what it prints to its customers. Not
                // derived, not normalised: `ДП Mustang+` and `upg95` are both already the name.
                Name = code,
                BrandFuelCode = code,
                // The pump is the column price, and it is the reference the customer's saving is
                // measured against. The customer price is a different thing and it is not written:
                // we know what the brand charges and nothing about what we pay, so nobody has
                // priced this fuel yet - an operator sets the cost and the margin, and every
                // reader derives the price the same way.
                PumpPricePerLiter = Math.Round(pump, 2, MidpointRounding.AwayFromZero),
                MarginUahPerLiter = null,
                AllowBelowCost = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.FuelTypes.Add(fuel);

            // History starts at the price it was born with, so the fuel's story begins at its creation
            // rather than at the first time an operator edited it.
            await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.PumpImport, actor, now, ct);

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
            await _context.SaveChangesAsync(ct);
        }

        return new UpgWogFuelCreated(brandId.ToString(), code, code, Math.Round(pump, 2, MidpointRounding.AwayFromZero));
    }
}
