using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Features.Orders.CreateCheckout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;

using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Features.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Brands;

/// <summary>
/// What a brand sells: its fuels, what each is priced at, and which nominal sizes exist.
/// <para>
/// <b>Split out of <see cref="BrandsController"/>, which is about the brand itself.</b> For nine
/// endpoints the file was called <c>BrandsController</c> while half of it priced fuel - the same name
/// covering "this network exists" and "this litre sells for 54.90", which is how <c>Brands</c> ended
/// up read as a filling Brand rather than a brand. A margin written here is a decision about a grade,
/// not about a network, so the routes and the name say so.
/// </para>
/// <para>
/// Route is <c>api/admin/brand-fuels</c> rather than the old nested <c>api/admin/brands/fuels/...</c>:
/// a fuel belongs to a brand but is addressed on its own - the fuel id is enough to price it, and a
/// caller holding it should not also have to know which brand it hangs under.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/brand-fuels")]
[Authorize(Policy = "Staff")]
public sealed class BrandFuelController : ControllerBase
{
    private readonly GetFuelPriceHistoryQueryHandler _getPriceHistory;
    private readonly GetFuelPriceAtQueryHandler _getPriceAt;
    private readonly AuditEventService _eventService;
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;

    // Every price this controller writes goes through here, so the history cannot be missed by the one
    // endpoint that forgets. See OfferPriceRecorder for why that is a service rather than a comment.
    private readonly OfferPriceRecorder _priceRecorder;

    public BrandFuelController(
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AuditEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        OfferPriceRecorder priceRecorder)
    {
        _getPriceHistory = getPriceHistory;
        _getPriceAt = getPriceAt;
        _eventService = eventService;
        _context = context;
        _notifications = notifications;
        _priceRecorder = priceRecorder;
    }

    private static readonly List<int> DefaultNominals = new() { 2, 3, 5, 10, 20, 50 };
    /// <summary>
    /// What one fuel was priced at, oldest first. The numbers the dashboard and reconciliation apply to
    /// historical orders, against the sequence that produced them - see
    /// <see cref="GetFuelPriceHistoryQueryHandler"/>.
    /// </summary>
    [HttpGet("{fuelId}/price-history")]
    public async Task<IActionResult> GetFuelPriceHistory(
        [FromRoute] string fuelId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getPriceHistory.HandleAsync(new GetFuelPriceHistoryQuery(fuelId, limit), ct));

    /// <summary>
    /// What one fuel was priced at at a given instant — the question an order line from last month asks,
    /// which a series clamped to the newest <c>limit</c> rows cannot answer.
    /// <para>
    /// Returns null (200 with an empty body) when no price had taken effect by then, which is a real
    /// answer: the fuel did not exist, or was not priced yet. Not a 404 — nothing is missing here.
    /// </para>
    /// </summary>
    [HttpGet("{fuelId}/price-at")]
    public async Task<IActionResult> GetFuelPriceAt(
        [FromRoute] string fuelId,
        [FromQuery] DateTime asOf,
        CancellationToken ct = default) =>
        Ok(await _getPriceAt.HandleAsync(new GetFuelPriceAtQuery(fuelId, asOf), ct));
    [HttpPost("brands/{id}/fuels")]
    public async Task<IActionResult> AddFuel([FromRoute] string id, [FromBody] CreateFuelRequest request, CancellationToken ct)
    {
        // Required for the same reason as on UpdateFuel: every package created below inherits it,
        // so an unstated flag would default to "sell this".
        if (!request.IsTestData.HasValue)
            return BadRequest(new
            {
                message = "isTestData is required. Set it to true for rehearsal data, or false if this "
                          + "is a real product.",
                field = nameof(request.IsTestData)
            });

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

        // One fuel type per name per Brand, refused rather than accepted as a second row. This fuel
        // id is generated here, so nothing about the write collides on a duplicate the way it did when
        // the id was the brand-encoded 'okko-95' - and a twin is not a cosmetic duplicate: it becomes a
        // second offer, the storefront lists one fuel at two prices, and the voucher importer resolves
        // the fuel by name with FirstOrDefault (OkkoFuelClassifier/WogFuelClassifier), so the twin
        // without the imported pump price is the one a real voucher can land on. The database refuses
        // the same thing with ux_fuel_types_Brand_name; this says which fuel already owns the name.
        var duplicate = await _context.FuelTypes
            .Where(f => f.BrandId == id && f.Name == request.Name)
            .Select(f => f.Id)
            .FirstOrDefaultAsync(ct);

        if (duplicate is not null)
        {
            return Conflict(new
            {
                message = $"'{request.Name}' already exists at {Brand.Name}. Price it with PUT "
                          + $"fuels/{duplicate} instead of adding it twice: a second row for the same "
                          + "fuel is a second offer, and a voucher can then be priced against whichever "
                          + "of the two the importer happens to pick.",
                fuelTypeId = duplicate,
                field = nameof(request.Name)
            });
        }

        // The same question asked of the code rather than the name, and refused in the same shape.
        // A code is the half of a fuel's identity that is not free text, so two fuels claiming one at
        // the same network is the ambiguity the catalogue import would resolve arbitrarily - and the
        // index below would answer 23505 naming neither fuel.
        var brandFuelCode = FuelTypeEntity.NormaliseBrandFuelCode(request.BrandFuelCode);
        if (brandFuelCode is not null)
        {
            var codeOwner = await _context.FuelTypes
                .Where(f => f.BrandId == id && f.BrandFuelCode == brandFuelCode)
                .Select(f => f.Id)
                .FirstOrDefaultAsync(ct);

            if (codeOwner is not null)
            {
                return Conflict(new
                {
                    message = $"Brand fuel code '{brandFuelCode}' is already taken at {Brand.Name} by fuel "
                              + $"{codeOwner}. A code identifies one fuel per network: two fuels claiming "
                              + "it is what makes a catalogue import pick the wrong row.",
                    fuelTypeId = codeOwner,
                    field = nameof(request.BrandFuelCode)
                });
            }
        }

        if (!request.IsTestData.Value)
        {
            var token = RehearsalMarkers.MatchedToken(null, request.Name);
            if (token is not null)
            {
                return BadRequest(new
                {
                    message = $"The name contains \"{token}\", which reads as rehearsal data, but "
                              + "isTestData is false. Set it to true, or rename the fuel if this is "
                              + "a real product.",
                    field = nameof(request.IsTestData)
                });
            }
        }

        var finalPerLiter = FuelPricing.FinalPerLiter(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);

        // Slice-3 hard block at the operator write path: refuse to persist a below-cost price
        // unless a manager has consciously opted this supplier+fuel in as a loss-leader.
        var belowCost = FuelPricing.IsBelowCost(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);
        if (belowCost && !request.AllowBelowCost)
            return BadRequest(new { code = BelowCostSaleBlockedException.Code, message = "Price is below supplier cost; enable the below-cost opt-in to save it." });

        // base = pump/reference (колонка) when known, else legacy final + min discount;
        // discount = what the customer actually pays (final).
        var baseUahPerLiter = request.PumpPricePerLiter ?? (finalPerLiter + request.MinDiscountPerLiter);

        var fuel = new FuelTypeEntity
        {
            Id = Guid.CreateVersion7().ToString(),
            BrandId = id,
            Name = request.Name,
            BrandFuelCode = brandFuelCode,
            // base_price / discount_price are UAH per liter, decimal to the kopeck (spec §12, #90):
            // round to 2dp, never store kopecks-as-integer (that once made the app show 8492.00).
            BasePrice = Math.Round(baseUahPerLiter, 2, MidpointRounding.AwayFromZero),
            DiscountPrice = Math.Round(finalPerLiter, 2, MidpointRounding.AwayFromZero),
            // The margin is born on the fuel. It used to be written only to the offer below, because the
            // offer was where every reader found it; now the fuel is where every reader looks, and a fuel
            // created without one prices at cost - silently, and correctly by the formula.
            MarginUahPerLiter = request.MarginUahPerLiter,
            AllowBelowCost = request.AllowBelowCost,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelTypes.Add(fuel);
        await _context.SaveChangesAsync(ct);

        var litersToCreate = request.PackageLiters;
        if (litersToCreate.Count == 0)
        {
            var existingFuelIds = await _context.FuelTypes
                .Where(f => f.BrandId == id && f.Id != fuel.Id)
                .Select(f => f.Id).ToListAsync(ct);
            litersToCreate = await _context.FuelNominals
                .Where(n => existingFuelIds.Contains(n.FuelTypeId))
                .Select(n => (int)n.Liters)
                .Distinct()
                .OrderBy(l => l)
                .ToListAsync(ct);
        }

        if (litersToCreate.Count == 0)
        {
            litersToCreate = DefaultNominals;
        }

        // One offer and N volumes, not N packages. Every per-litre knob the request carries is a
        // property of WHAT IS BEING SOLD, not of how much of it: a 5 L bottle and a 50 L canister of
        // the same fuel at the same Brand have one price per litre and therefore one offer. The old
        // row-per-volume shape wrote that same price N times and could not stop the copies drifting -
        // which is what the backfill had to reconcile when the split landed.
        var pool = DataKinds.ForPool(request.IsTestData.Value);
        var offer = new FuelOffer
        {
            Id = OfferKeys.For(fuel.Id, pool),
            BrandId = id,
            FuelTypeId = fuel.Id,
            SupplierPricePerLiter = request.SupplierPricePerLiter,
            // No margin here: it belongs to the fuel, set above. Repeating it on the offer is what
            // this column was, and two copies of one number is how they drift.
            MarginPercent = request.MarginPercent,
            // No FinalPricePerLiter: the offer stopped storing the derived figure on 2026-10-17.
            // inalPerLiter above is still used - to price the volumes this creates, and in the audit
            // payload - so the formula is evaluated once here and never written down.
            PumpPricePerLiter = request.PumpPricePerLiter,
            MinDiscountPerLiter = request.MinDiscountPerLiter,
            DataKind = pool,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _context.FuelOffers.Add(offer);

        // A new offer has no previous price, so this row is not a change but the price it was born with
        // - the same as every later row's role, and the reason a fuel's history starts at its creation
        // rather than at the first time somebody edited it.
        await _priceRecorder.RecordAsync(offer, OfferPriceReasons.Operator, GetUserId(), ct: ct);

            foreach (var liters in litersToCreate)
        {
            // The sizes are the fuel's, stated once. No price override either: a fuel added through
            // this screen is priced like itself, and the "before" price the storefront strikes
            // through is recomputed from the pump price via FuelPricing.OriginalPackagePrice
            // (planning #73), so the supplier cost never leaks to anonymous callers.
            _context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = fuel.Id,
                Liters = liters,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { fuel.Id, fuel.Name, request.SupplierPricePerLiter, request.MarginUahPerLiter, request.PumpPricePerLiter, request.MinDiscountPerLiter, FinalPricePerLiter = finalPerLiter });
        await _eventService.RecordEventAsync(
            "Fuel", fuel.Id, "FuelAdded",
            null, newValue, userId, GetUserName(),
            $"{Brand.Name} / {fuel.Name}: added at {finalPerLiter:F2} UAH/L",
            id,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(Brand.Name, fuel.Name, request.SupplierPricePerLiter, finalPerLiter, deliberate: true, ct);

        return Created($"/api/admin/brand-fuels/{fuel.Id}", new { id = fuel.Id });
    }

[HttpPut("{fuelId}")]
    public async Task<IActionResult> UpdateFuel([FromRoute] string fuelId, [FromBody] BrandFuelDto request, CancellationToken ct)
    {
        // Required, because the flag is written to every package of this fuel below. Answering with
        // a null would mean "the caller did not say", and the old non-nullable bool resolved that to
        // false - which publishes a rehearsal fuel when someone edits its margin from a client that
        // omits the field. There is no safe default for the one field that decides sellability, so
        // there is no default.
        if (!request.IsTestData.HasValue)
            return BadRequest(new
            {
                message = "isTestData is required. This edit rewrites the rehearsal flag on every "
                          + "package of the fuel, so it cannot be left unstated.",
                field = nameof(request.IsTestData)
            });

        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId, ct);
        if (fuel is null) return NotFound();

        // Offer existence - not package existence - is what decides whether this edit seeds defaults,
        // and the pool is read off the incoming request because that is the pool the fuel is moving
        // INTO. A fuel with a rehearsal offer and no live one is being created live here, and must be
        // seeded like one.
        var pool = DataKinds.ForPool(request.IsTestData.Value);

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
                return BadRequest(new
                {
                    message = $"The name contains \"{token}\", which reads as rehearsal data, but "
                              + "isTestData is false. Set it to true, or rename the fuel if this is "
                              + "a real product.",
                    field = nameof(request.IsTestData)
                });
            }
        }

        var finalPerLiter = FuelPricing.FinalPerLiter(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);

        // Slice-3 hard block: reject a below-cost price unless this supplier+fuel is opted in.
        var belowCost = FuelPricing.IsBelowCost(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);
        if (belowCost && !request.AllowBelowCost)
            return BadRequest(new { code = BelowCostSaleBlockedException.Code, message = "Price is below supplier cost; enable the below-cost opt-in to save it." });

        // base = pump/reference (колонка) when known, else legacy final + min discount.
        var baseUahPerLiter = request.PumpPricePerLiter ?? (finalPerLiter + request.MinDiscountPerLiter);

        var offerId = OfferKeys.For(fuel.Id, pool);
        var offer = await _context.FuelOffers.FirstOrDefaultAsync(o => o.Id == offerId, ct);

        // Flipping isTestData is not an update any more, and pretending otherwise is the bug this shape
        // removes. `data_kind` is part of the offer's identity - UNIQUE (brand_id, fuel_type_id,
        // data_kind) - so the same fuel at the same Brand has TWO offers, one per pool, and they are
        // different rows rather than one row with a flag. Moving a fuel from real to rehearsal
        // therefore means moving its volumes onto the rehearsal offer and archiving the real one.
        var otherPool = pool == DataKinds.Real ? DataKinds.Qa : DataKinds.Real;
        var otherOfferId = OfferKeys.For(fuel.Id, otherPool);
        var otherOffer = await _context.FuelOffers.FirstOrDefaultAsync(o => o.Id == otherOfferId, ct);

        // Whether this request is the one that made the offer. An entity it created is already
        // Added; calling Update on it turns that into an UPDATE against a row that is not there yet,
        // which the database answers with a concurrency failure.
        var offerCreatedNow = offer is null;

        if (offer is null)
        {
            offer = new FuelOffer
            {
                Id = offerId,
                BrandId = fuel.BrandId,
                FuelTypeId = fuel.Id,
                DataKind = pool,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _context.FuelOffers.Add(offer);
        }

var seededNominals = await _context.FuelNominals
    .AnyAsync(n => n.FuelTypeId == fuel.Id, ct);
    if (!seededNominals)
    {
    _context.FuelNominals.AddRange(DefaultNominals.Select(liters => new FuelNominal
    {
        FuelTypeId = fuel.Id,
        Liters = liters,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    }));
    }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == fuel.BrandId, ct);
        var BrandName = Brand?.Name ?? "?";

        // Snapshot BEFORE anything is written below. Reading "old" from `otherOffer` looks right when the
        // edit is flipping a fuel between pools - the other pool's offer is what it had - and silently
        // records nothing at all otherwise, because on an ordinary reprice `otherOffer` is null. The
        // audit then reads "supplier  -> 100.00" and the operator has no record of what they changed.
        var previousOffer = offer ?? otherOffer;
        var oldSupplierPrice = previousOffer?.SupplierPricePerLiter;
        // The fuel already carries its current margin at this point - the snapshot below runs before any
        // write - and the offer's copy is no longer written, so this is the only "old" that still exists.
        var oldMarginPrice = fuel.MarginUahPerLiter;
        // Computed from the offer's own knobs and the fuel's margin rather than read from a column: the
        // audit payload records what the customer paid per litre, and that number is still a fact even
        // though nothing stores it.
        var oldFinalPrice = previousOffer is null
            ? null
            : FuelPricing.FinalPerLiterOrNull(
                previousOffer.SupplierPricePerLiter,
                oldMarginPrice,
                previousOffer.PumpPricePerLiter,
                previousOffer.MinDiscountPerLiter);
        var oldValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            SupplierPricePerLiter = oldSupplierPrice,
            MarginUahPerLiter = oldMarginPrice,
            MarginPercent = previousOffer?.MarginPercent,
            FinalPricePerLiter = oldFinalPrice,
            fuel.BasePrice,
            fuel.DiscountPrice
        });

        // The per-litre knobs are written to the offer, once, instead of to every package row. The
        // volume only carries the frozen package total, which is a function of litres - and litres
        // never change when the price does, so this recomputes cleanly.
        offer.SupplierPricePerLiter = request.SupplierPricePerLiter;
        // The margin's home is the fuel, and it is written there first. A margin is a fact about a
        // fuel rather than about a sale of it — the same diesel carries the same margin whether we
        // sell two litres or a hundred — so on the offer it was per (brand × fuel × pool), which meant
        // two pools of one fuel could quote two prices.
        fuel.MarginUahPerLiter = request.MarginUahPerLiter;
        // The offer's copy of the margin is gone as of this change: every reader - storefront, checkout,
        // dashboard, below-cost guard and the price recorder - takes it from the fuel, so a second
        // column holding it would be a number nobody reads and one writer could forget.
        offer.MarginPercent = request.MarginPercent;
        offer.PumpPricePerLiter = request.PumpPricePerLiter;
        offer.MinDiscountPerLiter = request.MinDiscountPerLiter;
        // offer.FinalPricePerLiter = finalPerLiter used to live here. The customer price is
        // min(cost + profit, pump - minDiscount) over the four lines above, so writing it down beside
        // them was a second copy of the same number - and one the audit trail then compared against.
        offer.UpdatedAtUtc = DateTime.UtcNow;

        // The loop that restated every volume's total after a reprice is gone, and its absence is the
        // improvement rather than a gap: a volume's price is now the offer's rate x its litres unless
        // somebody set an override, so there is nothing to restate - and an override is no longer erased
        // by the next pump import, which is what made this endpoint dangerous to touch.

        // The history row, before the save that persists the offer - and it also stamps
        // price_updated_at / price_updated_by_user_id, which this endpoint previously left at their
        // creation-time values, so the audit trail could not say when an operator repriced a fuel.
        await _priceRecorder.RecordAsync(offer, OfferPriceReasons.Operator, GetUserId(), ct: ct);

        // Archive rather than delete the offer we just emptied: it carries the pricing history that
        // `fuel_packages.is_deleted` rows carried before the split, and a delete here would take the
        // volumes' old home with it.
        if (otherOffer is not null && otherOffer.Id != offer.Id)
        {
            otherOffer.IsDeleted = true;
            otherOffer.DeletedAtUtc = DateTime.UtcNow;
            otherOffer.UpdatedAtUtc = DateTime.UtcNow;
        }

        fuel.Name = request.Name;
        // base = pump/reference (колонка) when known, else legacy final + min discount;
        // discount = customer price (final).
        fuel.BasePrice = Math.Round(baseUahPerLiter, 2, MidpointRounding.AwayFromZero);
        fuel.DiscountPrice = Math.Round(finalPerLiter, 2, MidpointRounding.AwayFromZero);
        fuel.AllowBelowCost = request.AllowBelowCost;
        fuel.UpdatedAtUtc = DateTime.UtcNow;

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

                if (codeOwner is not null)
                {
                    return Conflict(new
                    {
                        message = $"Brand fuel code '{wanted}' is already taken at {fuel.BrandId} by fuel "
                                  + $"{codeOwner}. A code identifies one fuel per network.",
                        fuelTypeId = codeOwner,
                        field = nameof(request.BrandFuelCode)
                    });
                }
            }

            fuel.BrandFuelCode = wanted;
        }


        if (!offerCreatedNow) _context.FuelOffers.Update(offer);
        _context.FuelTypes.Update(fuel);

        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            request.SupplierPricePerLiter,
            request.MarginUahPerLiter,
            request.MarginPercent,
            request.PumpPricePerLiter,
            request.MinDiscountPerLiter,
            FinalPricePerLiter = finalPerLiter
        });

        var changes = new List<string>();
        var oldFuelName = fuel.Name;
        if (oldFuelName != request.Name)
            changes.Add($"name {oldFuelName} → {request.Name}");
        if (oldSupplierPrice != request.SupplierPricePerLiter)
            changes.Add($"supplier {oldSupplierPrice:F2} → {request.SupplierPricePerLiter:F2}");
        if (oldMarginPrice != request.MarginUahPerLiter)
            changes.Add($"margin {oldMarginPrice:F2} → {request.MarginUahPerLiter:F2}");
        if (oldFinalPrice != finalPerLiter)
            changes.Add($"final {oldFinalPrice:F2} → {finalPerLiter:F2}");

        var summary = changes.Count > 0
            ? $"{BrandName} / {fuel.Name}: {string.Join(", ", changes)}"
            : $"{BrandName} / {fuel.Name}: updated";

        await _eventService.RecordEventAsync(
            "Fuel", fuelId, "PriceChanged",
            oldValue, newValue, userId, GetUserName(),
            summary,
            fuel.BrandId,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(BrandName, fuel.Name, request.SupplierPricePerLiter, finalPerLiter, deliberate: true, ct);

        return Ok(new { success = true });
    }

    [HttpDelete("{fuelId}")]
    public async Task<IActionResult> DeleteFuel([FromRoute] string fuelId, CancellationToken ct)
    {
        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId, ct);
        if (fuel is null) return NotFound();

        // A fuel may only be archived once it is idle: every one of its vouchers spent or expired.
        //
        // Settled with the operator, 2026-10-17. The two conditions are separate and both matter. The
        // brand dropping a fuel says it is no longer sold, which only the price sheet can establish —
        // that half is `ImportOkkoCatalogueCommandHandler`'s to prove, and it never guesses. This half
        // is the one that protects the money: a fuel archived while customers still hold its vouchers
        // leaves those vouchers pointing at a row the catalogue no longer shows, so they cannot be
        // priced (#217). Refusing here is the cheap place to catch it — after the fact, the vouchers
        // are already unusable and the only repair is un-archiving and reconciling by hand.
        var blockingStatuses = VoucherStatuses.Blocking;
        var liveVouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .Where(v => v.FuelTypeId == fuelId && !v.IsDeleted && blockingStatuses.Contains(v.Status))
            .Select(v => new { v.Id, v.Status, v.VoucherNumber, v.Liters })
            .Take(6)
            .ToListAsync(ct);

        if (liveVouchers.Count > 0)
        {
            var total = await _context.FuelVouchers
                .IgnoreQueryFilters()
                .CountAsync(v => v.FuelTypeId == fuelId && !v.IsDeleted && blockingStatuses.Contains(v.Status), ct);

            return Conflict(new
            {
                error = $"«{fuel.Name}» не можна архівувати: {total} талон(ів) ще активні. " +
                        "Архівувати можна лише пальне, у якого всі талони використані або прострочені.",
                activeVoucherCount = total,
                examples = liveVouchers.Select(v => new { v.VoucherNumber, status = v.Status.ToString(), v.Liters }),
            });
        }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == fuel.BrandId, ct);
        var BrandName = Brand?.Name ?? "?";
        var fuelName = fuel.Name;

        // Same as the brand branch: this path archives rows rather than reading them.
        var offerIdsToArchive = await _context.FuelOffers
            .Where(o => o.FuelTypeId == fuelId)
            .Select(o => o.Id).ToListAsync(ct);
        var offersToArchive = await _context.FuelOffers
            .Where(o => offerIdsToArchive.Contains(o.Id))
            .ToListAsync(ct);

        // Archive rather than remove - same reasoning as the brand branch above: the cascade from
        // fuel_types reaches fuel_vouchers (#217).
        var archivedAt = DateTime.UtcNow;
        // The sizes are not archived: they belong to the fuel, and the fuel row below is the one that
        // says no. Archiving the offers is what takes the fuel off the shelf, and a live offer would
        // put it back the moment a nominals edit touched the fuel.
        foreach (var offer in offersToArchive)
        {
            offer.IsDeleted = true;
            offer.DeletedAtUtc = archivedAt;
        }

        fuel.IsDeleted = true;
        fuel.DeletedAtUtc = archivedAt;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Fuel", fuelId, "FuelRemoved",
            JsonSerializer.Serialize(new { fuelName, BrandName }), "{}",
            userId, GetUserName(),
            $"{BrandName} / {fuelName}: removed",
            fuel.BrandId,
            ct);

        return Ok(new { success = true });
    }

    [HttpPut("brands/{id}/nominals")]
    public async Task<IActionResult> UpdateNominals([FromRoute] string id, [FromBody] List<int> nominals, CancellationToken ct)
    {
        // Rejected here rather than left to ck_fuel_packages_liters_positive. The request body binds
        // List<int> straight off the wire, so -10 used to create a package priced at -10 x the
        // per-litre rate and the storefront listed it. A 23514 from the database would be correct but
        // opaque - it names neither the offending nominal nor this endpoint - so the caller gets a
        // message that says which value and why.
        var invalid = nominals.Where(n => n <= 0).Distinct().OrderBy(n => n).ToList();
        if (invalid.Count > 0)
        {
            return BadRequest(new
            {
                message = "A nominal volume must be a positive number of litres.",
                invalid
            });
        }

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

var BrandFuels = await _context.FuelTypes.Where(f => f.BrandId == id).ToListAsync(ct);
    var fuelIds = BrandFuels.Select(f => f.Id).ToList();

    // The sizes a fuel is sold in are the operator's answer, written once per fuel rather than once
    // per offer. Read AsTracking: the rows below are updated in place, and the context defaults to
    // NoTracking - without it the edit would persist nothing (NoTrackingMutationGuardTests).
    var existingNominals = await _context.FuelNominals
        .AsTracking()
        .Where(n => fuelIds.Contains(n.FuelTypeId))
        .ToListAsync(ct);

    var oldValue = JsonSerializer.Serialize(
        existingNominals.Select(n => (int)n.Liters).Distinct().OrderBy(l => l).ToList());

        // #217: keep what survives, archive what leaves, add what is new.
        //
        // This used to RemoveRange every package and recreate the whole set with fresh ids. Two
        // things were wrong with that: order_line_items point at packages, so removing one took the
        // record of what a customer bought with it, and a fresh id on every price edit meant the
        // package an order referenced was never the package in the catalog. Reusing a package that
        // stays keeps its id, so historical rows keep resolving to the thing they bought.
        //
        // Pricing is unchanged: the cost and the pump are the offer's, the margin is the fuel's, and only the
        // derived package total is rewritten on each volume.
        var wanted = nominals.Distinct().ToHashSet();
        var now = DateTime.UtcNow;

// The sizes a fuel is sold in are the operator's answer, written once per fuel rather than once
    // per offer. The volume loops that used to follow are gone: `fuel_offer_volumes` was a second
    // copy of this same answer, and keeping both in step was the whole cost of having it.
    foreach (var fuel in BrandFuels)
    {
        foreach (var leaving in existingNominals.Where(n => n.FuelTypeId == fuel.Id && !wanted.Contains((int)n.Liters)))
        {
            _context.FuelNominals.Remove(leaving);
        }

        foreach (var liters in wanted.Where(l => existingNominals.All(n => n.FuelTypeId != fuel.Id || n.Liters != l)))
        {
            _context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = fuel.Id,
                Liters = liters,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
    }

    await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(nominals.OrderBy(n => n).ToList());
        await _eventService.RecordEventAsync(
            "Nominal", id, "NominalSetChanged",
            oldValue, newValue, userId, GetUserName(),
            $"{Brand.Name}: nominals changed [{string.Join(", ", nominals.OrderBy(n => n))}]",
            id,
            ct);

        return Ok(new { success = true });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} {last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}

