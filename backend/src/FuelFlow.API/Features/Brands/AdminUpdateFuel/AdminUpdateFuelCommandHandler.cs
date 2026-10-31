using System.Text.Json;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminUpdateFuel;

/// <summary>
/// Reprices a fuel, and records what the price was before and what it is now.
/// </summary>
/// <remarks>
/// Two things are deliberately not this handler's to do. It does not move a fuel between the pools
/// - the sizes decide the pool, and a price edit that changed the pool would publish or hide a
/// product by accident. And it does not drop the fuel's code: an edit that silently cleared it
/// would leave a fuel a catalogue import can no longer match to the network's price sheet.
/// </remarks>
public sealed class AdminUpdateFuelCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    // Every price this handler writes goes through here, so the history cannot be missed by the one
    // endpoint that forgets. See PriceHistoryRecorder for why that is a service rather than a comment.
    private readonly PriceHistoryRecorder _priceRecorder;

    public AdminUpdateFuelCommandHandler(
        ApplicationDbContext context,
        AuditEventService eventService,
        PriceHistoryRecorder priceRecorder)
    {
        _context = context;
        _eventService = eventService;
        _priceRecorder = priceRecorder;
    }

    public async Task<AdminUpdateFuelResult> HandleAsync(AdminUpdateFuelCommand command, CancellationToken ct = default)
    {
        var request = command.Fuel;

        // Required, because the flag is written to every package of this fuel below. Answering with
        // a null would mean "the caller did not say", and the old non-nullable bool resolved that to
        // false - which publishes a rehearsal fuel when someone edits its margin from a client that
        // omits the field. There is no safe default for the one field that decides sellability, so
        // there is no default.
        if (!request.IsTestData.HasValue)
            return new AdminUpdateFuelResult
            {
                Error = "isTestData is required. It names the pool the fuel is sold in, and a fuel "
                          + "with no sizes yet is seeded in that pool - so it cannot be left unstated.",
                Code = BrandRefusalCodes.TestDataRequired,
                Field = nameof(request.IsTestData)
            };

        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == command.FuelId, ct);
        if (fuel is null) return new AdminUpdateFuelResult { NotFound = true };

        // Whether the fuel has any SIZES is what decides whether this edit seeds defaults - not
        // whether it has packages. The pool is read off the incoming request because that is the pool
        // the fuel is moving INTO, and a fuel with no sizes in it must be seeded like one.
        var pool = DataKinds.ForPool(request.IsTestData.Value);

        // The pool a fuel is sold in lives on its sizes, so a flag that disagrees with them is a
        // request the price edit cannot carry out. Saying so is the point: a silent no-op here is how
        // an operator comes to believe a live fuel was hidden, or a rehearsal one was published.
        var sizes = await _context.FuelNominals
            .Where(n => n.FuelTypeId == fuel.Id)
            .ToListAsync(ct);
        if (sizes.Count > 0)
        {
            var soldAsQa = sizes.All(n => n.DataKind == DataKinds.Qa);
            if (soldAsQa != request.IsTestData.Value)
            {
                return new AdminUpdateFuelResult
                {
                    Conflict = true,
                    Error = $"This fuel is sold in the {(soldAsQa ? "QA" : "live")} pool, and its sizes "
                              + "decide that. A price edit does not move them - change the size list to "
                              + "change the pool.",
                    Code = BrandRefusalCodes.PoolMismatch,
                    Field = nameof(request.IsTestData)
                };
            }
        }

        // A fuel whose name reads as rehearsal cannot be flipped to live by an edit. The flag is the
        // real control - an operator may innocently name a grade "Test-A95" - but the silent case,
        // where the name says rehearsal and the flag says sell, is what reached the public storefront.
        //
        // The NAME only, deliberately: a fuel's id is generated here (Guid.CreateVersion7), so an
        // operator never chooses it and matching on it would fire on ids nobody picked. The package
        // endpoint is the opposite - it takes a caller-supplied id, which is where
        // "test-1uah-okko-a95eu" came from - so there the id is checked too.
        if (!request.IsTestData.Value)
        {
            var token = RehearsalMarkers.MatchedToken(null, request.Name ?? fuel.Name);
            if (token is not null)
            {
                return new AdminUpdateFuelResult
                {
                    Error = $"The name contains \"{token}\", which reads as rehearsal data, but "
                              + "isTestData is false. Set it to true, or rename the fuel if this is "
                              + "a real product.",
                    Code = BrandRefusalCodes.RehearsalName,
                    Token = token,
                    Field = nameof(request.IsTestData)
                };
            }
        }

        // The price, or null when we cannot know it - see the create path above.
        var finalPerLiter = FuelPricing.FinalPerLiterOrNull(
            request.SupplierPricePerLiter, request.MarginUahPerLiter);

        // The numbers themselves, refused here rather than by the database. The checks the database
        // makes are real (ck_fuel_types_margin_signs, ck_fuel_types_allow_below_cost_needs_a_cost),
        // but a check constraint answers "23514" and names neither the knob nor the operator, and a
        // zero cost is one the constraints let through and the pricing must not: a litre we paid
        // nothing for has no cost to guard a sale against.
        if (FuelEconomics.Fault(request.SupplierPricePerLiter, request.MarginUahPerLiter, request.AllowBelowCost)
            is { } fault)
        {
            return new AdminUpdateFuelResult
            {
                Error = fault.Error,
                Code = BrandRefusalCodes.ForEconomicsFault(fault.Field),
                Field = fault.Field
            };
        }

        // A fuel that has live stock on the shelf keeps a price of its own. The stock is already on
        // sale, and the price the customer is charged is computed from the fuel's cost and margin -
        // so taking either of them away here does not reprice the stock, it strands it: the storefront
        // stops listing it as buyable and the sale is refused at checkout. A fuel with no stock behind
        // it may be unpriced: it is a card nobody buys yet.
        var liveStock = await _context.FuelVouchers
            .Where(VoucherDeletionGuard.IsStock)
            .AnyAsync(v => v.FuelTypeId == fuel.Id && v.DataKind != DataKinds.Qa, ct);

        if (liveStock)
        {
            var resulting = FuelEconomics.State(request.SupplierPricePerLiter, request.MarginUahPerLiter);
            if (!resulting.IsPriced)
            {
                return new AdminUpdateFuelResult
                {
                    Error = $"This fuel has live stock on the shelf, so it must keep a price of its own: "
                              + $"{FuelEconomics.Missing(resulting)}. Set the cost and the margin, or take "
                              + "the stock off the shelf first.",
                    Code = BrandRefusalCodes.LiveStockNeedsPrice,
                    Gap = resulting.Gap,
                    Field = resulting.HasCost ? nameof(request.MarginUahPerLiter) : nameof(request.SupplierPricePerLiter)
                };
            }
        }

        // The reference and the price are computed, so there is nothing to write for them here either.
        // The operator's four knobs are the whole truth: the cost, the margin, the column price and
        // the opt-in.

        // There is no offer to find, create or flip: the price is the fuel's, and the SIZES are the
        // fuel's too - the size list is what the two pools differ in.
        var seededNominals = await _context.FuelNominals
            .AnyAsync(n => n.FuelTypeId == fuel.Id, ct);
        if (!seededNominals)
        {
            _context.FuelNominals.AddRange(DefaultNominalVolumes.Liters.Select(liters => new FuelNominal
            {
                FuelTypeId = fuel.Id,
                // In the pool the request named: the pool is part of a size's identity, and a size in the
                // wrong pool is a size in the wrong catalogue.
                DataKind = pool,
                Liters = liters,
                CreatedAtUtc = SystemClock.UtcNow,
                UpdatedAtUtc = SystemClock.UtcNow,
            }));
        }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == fuel.BrandId, ct);
        var BrandName = Brand?.Name ?? "?";

        // Snapshot BEFORE anything is written below. The "before" is the fuel's own figures, which
        // is where every price figure lives now; there is no second copy to fall back to for the old one.
        var oldFuelName = fuel.Name;
        var oldSupplierPrice = fuel.SupplierPricePerLiter;
        var oldMarginPrice = fuel.MarginUahPerLiter;
        // audit payload records what the customer paid per litre, and that number is still a fact even
        // though nothing stores it.
        var oldFinalPrice = FuelPricing.FinalPerLiterOrNull(
            oldSupplierPrice,
            oldMarginPrice);

        var oldValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            SupplierPricePerLiter = oldSupplierPrice,
            MarginUahPerLiter = oldMarginPrice,
            MarginPercent = fuel.MarginPercent,
            FinalPricePerLiter = oldFinalPrice,
            fuel.PumpPricePerLiter
        });

        // Every knob lands on the fuel. There is one price per grade, so there is no second
        // place to write it and no second writer that could forget.
        fuel.SupplierPricePerLiter = request.SupplierPricePerLiter;
        fuel.MarginUahPerLiter = request.MarginUahPerLiter;
        fuel.MarginPercent = request.MarginPercent;
        fuel.PumpPricePerLiter = request.PumpPricePerLiter;
        fuel.UpdatedAtUtc = SystemClock.UtcNow;
        // The loop that restated every volume's total after a reprice is gone, and its absence is the
        // improvement rather than a gap: a volume's price is now the fuel's rate x its litres, so there
        // is nothing to restate - and there is no per-size override left for the next pump import to
        // erase, which is what made this endpoint dangerous to touch.

        // The history row, before the save that persists the fuel - and it also stamps
        // price_updated_at / price_updated_by_user_id, which this endpoint previously left at their
        // creation-time values, so the audit trail could not say when an operator repriced a fuel.
        await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.Operator, command.UserId, ct: ct);

        // The other pool has nothing to archive: the price used to live on a row of its own, and the
        // price is the fuel's. What separates the pools now is the size list, handled above.
        fuel.Name = request.Name ?? fuel.Name;
        // The price the customer pays is computed from the knobs above, so there is nothing to write
        // for it here: the fuel carries the cost, the margin and the column price, and every reader
        // derives the rest the same way.
        fuel.AllowBelowCost = request.AllowBelowCost;
        fuel.UpdatedAtUtc = SystemClock.UtcNow;

        // Omitted leaves the code alone; an empty string clears it. An edit is a repricing, and a
        // repricing that silently dropped the code would leave a fuel that a catalogue import can no
        // longer match to the network's price sheet - which reads on screen as "we sell this fuel" and
        // on the site as one we stopped importing.
        if (request.BrandFuelCode is not null)
        {
            var wanted = FuelTypeEntity.NormaliseBrandFuelCode(request.BrandFuelCode);

            if (wanted != fuel.BrandFuelCode)
            {
                var codeOwner = await _context.FuelTypes
                    .Where(f => f.BrandId == fuel.BrandId && f.BrandFuelCode == wanted && f.Id != fuel.Id)
                    .Select(f => f.Id)
                    .FirstOrDefaultAsync(ct);

                if (codeOwner != Guid.Empty)
                {
                    return new AdminUpdateFuelResult
                    {
                        Conflict = true,
                        Error = $"Brand fuel code '{wanted}' is already taken at {fuel.BrandId} by fuel "
                                  + $"{codeOwner}. A code identifies one fuel per network.",
                        Code = BrandRefusalCodes.CodeTaken,
                        FuelTypeId = codeOwner,
                        Field = nameof(request.BrandFuelCode)
                    };
                }
            }

            fuel.BrandFuelCode = wanted;
        }

        _context.FuelTypes.Update(fuel);

        var newValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            request.SupplierPricePerLiter,
            request.MarginUahPerLiter,
            request.MarginPercent,
            request.PumpPricePerLiter,
            FinalPricePerLiter = finalPerLiter
        });

        var changes = new List<string>();
        if (oldFuelName != fuel.Name)
            changes.Add($"name {oldFuelName} → {fuel.Name}");
        if (oldSupplierPrice != request.SupplierPricePerLiter)
            changes.Add($"supplier {oldSupplierPrice:F2} → {request.SupplierPricePerLiter:F2}");
        if (oldMarginPrice != request.MarginUahPerLiter)
            changes.Add($"margin {oldMarginPrice:F2} → {request.MarginUahPerLiter:F2}");
        if (oldFinalPrice != finalPerLiter)
            changes.Add($"final {oldFinalPrice:F2} → {(finalPerLiter ?? request.PumpPricePerLiter ?? 0m):F2}");

        var summary = changes.Count > 0
            ? $"{BrandName} / {fuel.Name}: {string.Join(", ", changes)}"
            : $"{BrandName} / {fuel.Name}: updated";

        _eventService.RecordEvent(
            "Fuel", command.FuelId.ToString(), "PriceChanged",
            oldValue, newValue, command.UserId, command.UserName,
            summary,
            Brand!.Slug);

        // One save for the fuel, the price history and the audit row: the row and the change it
        // describes commit together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminUpdateFuelResult { Success = true };
    }
}
