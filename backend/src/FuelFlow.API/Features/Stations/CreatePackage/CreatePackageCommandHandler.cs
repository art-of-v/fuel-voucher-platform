using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Pricing;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.CreatePackage;

public sealed class CreatePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly OfferPriceRecorder _priceRecorder;

    public CreatePackageCommandHandler(ApplicationDbContext context, OfferPriceRecorder priceRecorder)
    {
        _context = context;
        _priceRecorder = priceRecorder;
    }

    /// <param name="ct">Cancellation.</param>
    /// <param name="actingUserId">
    /// The staff member the endpoint ran as, stamped on the price history this path appends. Optional,
    /// and null for a caller that has no user to name - the row is still written, because a price that
    /// moved is worth recording even when nobody can be blamed for it.
    /// </param>
    public async Task<CreatePackageResult> HandleAsync(
        CreatePackageCommand command,
        CancellationToken ct = default,
        Guid? actingUserId = null)
    {
        if (string.IsNullOrWhiteSpace(command.Package.Id) ||
            string.IsNullOrWhiteSpace(command.Package.StationId) ||
            string.IsNullOrWhiteSpace(command.Package.FuelTypeId))
            return new CreatePackageResult { Error = "Id, StationId and FuelTypeId are required" };

        var exists = await _context.FuelOfferVolumes.AnyAsync(x => x.Id == command.Package.Id, ct);
        if (exists)
            return new CreatePackageResult { Conflict = true, Error = $"Package with id '{command.Package.Id}' already exists" };

        var pool = DataKinds.ForPool(command.Package.IsTestData);
        var offerId = OfferKeys.For(command.Package.FuelTypeId, pool);

        // One supplier price per fuel, per pool. Every other write path copies the cost off the
        // existing offer (BrandsController seeds, prices and station creation), so this endpoint is the
        // only place where an offer can arrive carrying a cost of its own — and that is how a 0.01 L
        // test package at 5.00/L ended up beside 100.00/L packages, after which the fuel card reported
        // it healthy while the below-cost guard refused to sell the real fuel at all.
        //
        // Being the owner is not a way around a business rule, so this refuses rather than warns: the
        // caller has to price the fuel as a whole (UpdateFuel writes one cost to every package) or put
        // test fuel in its own fuel type.
        //
        // Scoped to the same pool the new package is in, because the rehearsal catalogue is priced
        // independently and refusing a legitimate 1 UAH rehearsal package because the live fuel costs
        // 50 is the bug, not the rule.
        var existingOffer = await _context.FuelOffers
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);

        var existingCosts = await _context.FuelOfferVolumes
            .Where(v => v.Offer.FuelTypeId == command.Package.FuelTypeId
                     && v.Offer.DataKind == pool
                     && v.Offer.SupplierPricePerLiter != null)
            .Select(v => new { v.Liters, v.Offer.SupplierPricePerLiter })
            .ToListAsync(ct);

        if (command.Package.SupplierPricePerLiter is { } incoming && existingCosts.Count > 0)
        {
            // Rounded to the column's scale so a value that only differs by representation cannot trip
            // the rule: the guard must refuse a real disagreement, not a rounding artefact.
            var expected = decimal.Round(existingCosts[0].SupplierPricePerLiter!.Value, 4);
            var offered = decimal.Round(incoming, 4);

            if (offered != expected)
            {
                var held = string.Join(", ", existingCosts
                    .OrderBy(c => c.Liters)
                    .Select(c => $"{decimal.Round(c.SupplierPricePerLiter!.Value, 4):0.####} at {c.Liters:0.##} L"));

                return new CreatePackageResult
                {
                    Conflict = true,
                    Error = $"This fuel already has a supplier price of {expected:0.####} UAH/L ({held}). "
                        + $"A package cannot arrive with a different cost of {offered:0.####} UAH/L: the fuel card reads one price "
                        + "for the whole fuel while the below-cost guard enforces it per package, so they would disagree. "
                        + "Price the fuel as a whole instead, or give test fuel its own fuel type."
                };
            }
        }

        var now = DateTime.UtcNow;

        // The offer is found or created. The caller-supplied id belongs to the VOLUME - it is the
        // package id that order lines and the storefront know - and the offer is keyed by what is sold,
        // not by whoever happened to create it first.
        if (existingOffer is null)
        {
            existingOffer = new FuelOffer
            {
                Id = offerId,
                StationId = command.Package.StationId,
                FuelTypeId = command.Package.FuelTypeId,
                DataKind = pool,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _context.FuelOffers.Add(existingOffer);
        }

        // The knobs go on the offer, so a package created here is priced by the same single source as one
        // created through the brand screen. Previously each package row carried its own copy and this is
        // exactly the endpoint that wrote a second, disagreeing copy.
        if (command.Package.SupplierPricePerLiter is not null)
            existingOffer.SupplierPricePerLiter = command.Package.SupplierPricePerLiter;
        if (command.Package.MarginUahPerLiter is not null)
            existingOffer.MarginUahPerLiter = command.Package.MarginUahPerLiter;
        if (command.Package.MarginPercent is not null)
            existingOffer.MarginPercent = command.Package.MarginPercent;
        // `command.Package.FinalPricePerLiter` used to be written straight onto the offer here,
        // unvalidated against the knobs above it — the one write path in the schema that could make the
        // stored per-litre price disagree with the cost and margin it was computed from. There is no
        // stored per-litre price to write any more; a caller that sends the field has it ignored, which
        // is also what happened to every other field ASP.NET does not know.
        if (command.Package.PumpPricePerLiter is not null)
            existingOffer.PumpPricePerLiter = command.Package.PumpPricePerLiter;
        if (command.Package.MinDiscountPerLiter is not null)
            existingOffer.MinDiscountPerLiter = command.Package.MinDiscountPerLiter;
        existingOffer.UpdatedAtUtc = now;

        // The history row for whatever price this leaves on the offer - a new offer's first price, or an
        // existing one the caller just re-costed. The recorder skips it when the six figures are
        // unchanged, which is the common case here: creating a package usually carries no price at all.
        await _priceRecorder.RecordAsync(existingOffer, OfferPriceReasons.Operator, actingUserId, now, ct);

        var volume = new FuelOfferVolume
        {
            Id = command.Package.Id,
            OfferId = offerId,
            Liters = command.Package.Liters,
            PackagePrice = command.Package.Price,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _context.FuelOfferVolumes.Add(volume);
        await _context.SaveChangesAsync(ct);

        // The response is still a package, because that is the contract this endpoint publishes: same
        // JSON, same id, same field names. The caller has no reason to know a volume and an offer
        // are what it is looking at.
        var fuelName = await _context.FuelTypes
            .Where(f => f.Id == command.Package.FuelTypeId)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(ct) ?? command.Package.FuelName;

        return new CreatePackageResult { Success = true, Package = volume.ToPackage(fuelName) };


    }
}

public sealed class CreatePackageResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public FuelPackage? Package { get; set; }
}