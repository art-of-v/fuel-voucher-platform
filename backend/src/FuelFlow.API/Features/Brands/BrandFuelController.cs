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
    // endpoint that forgets. See PriceHistoryRecorder for why that is a service rather than a comment.
    private readonly PriceHistoryRecorder _priceRecorder;

    public BrandFuelController(
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AuditEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        PriceHistoryRecorder priceRecorder)
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
        [FromRoute] Guid fuelId,
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
        [FromRoute] Guid fuelId,
        [FromQuery] DateTime asOf,
        CancellationToken ct = default) =>
        Ok(await _getPriceAt.HandleAsync(new GetFuelPriceAtQuery(fuelId, asOf), ct));
    [HttpPost("brands/{id}/fuels")]
    public async Task<IActionResult> AddFuel([FromRoute] Guid id, [FromBody] CreateFuelRequest request, CancellationToken ct)
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

        if (duplicate != Guid.Empty)
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

            if (codeOwner != Guid.Empty)
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

        // The price, or null when we cannot know it. A cost that is not stated is not a zero: the
        // model refuses to build a price out of a missing number, because "we do not know what we
        // paid" is not the same fact as "we paid nothing".
        var finalPerLiter = FuelPricing.FinalPerLiterOrNull(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);

        // Slice-3 hard block at the operator write path: refuse to persist a below-cost price
        // unless a manager has consciously opted this supplier+fuel in as a loss-leader.
        //
        // A fuel with no stated cost is NOT below cost - there is nothing to compare against - so it
        // is saved as it is: unpriced. The storefront shows it at the column with "not in stock", and
        // it cannot be sold until the cost is recorded. What it must never become is a fuel with a cost
        // of zero, which would price itself at its margin and blind the guard.
        var belowCost = request.SupplierPricePerLiter is { } knownCost
                        && request.MarginUahPerLiter is { } knownMargin
                        && FuelPricing.IsBelowCost(
                            knownCost, knownMargin, request.PumpPricePerLiter, request.MinDiscountPerLiter);
        if (belowCost && !request.AllowBelowCost)
            return BadRequest(new { code = BelowCostSaleBlockedException.Code, message = "Price is below supplier cost; enable the below-cost opt-in to save it." });

        // base = pump/reference (колонка) when known, else legacy final + min discount;
        // discount = what the customer actually pays (final).
        //
        // With no stated cost there is no final price, so the fuel is recorded at the column: the
        // price the driver pays, with no discount on it. That is a true statement about a fuel we
        // have not costed, and it is what the storefront shows.
        var baseUahPerLiter = request.PumpPricePerLiter
                              ?? (finalPerLiter is { } known
                                  ? known + request.MinDiscountPerLiter
                                  : request.MinDiscountPerLiter);
        var discountUahPerLiter = finalPerLiter ?? baseUahPerLiter;

        var fuel = new FuelTypeEntity
        {
            Id = Guid.CreateVersion7(),
            BrandId = id,
            Name = request.Name,
            BrandFuelCode = brandFuelCode,
            // base_price / discount_price are UAH per liter, decimal to the kopeck (spec §12, #90):
            // round to 2dp, never store kopecks-as-integer (that once made the app show 8492.00).
            BasePrice = Math.Round(baseUahPerLiter, 2, MidpointRounding.AwayFromZero),
            DiscountPrice = Math.Round(discountUahPerLiter, 2, MidpointRounding.AwayFromZero),
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

// Every per-litre knob the request carries is a property of WHAT IS BEING SOLD, not of how much of
    // it: a 5 L bottle and a 50 L canister of the same fuel have one price per litre. They used to
    // be written on an offer, because an offer per pool could hold two prices for one grade; with
    // the pool on the size list they are written on the fuel and there is no second place to keep
    // them in step.
    var pool = DataKinds.ForPool(request.IsTestData.Value);
    // The cost and the guaranteed discount are not nullable on the request: an operator who leaves
    // them blank means zero, and the fuel records that. Only the ceiling and the margin percent are
    // "not stated", and those stay null rather than being invented as zero.
    fuel.SupplierPricePerLiter = request.SupplierPricePerLiter;
    fuel.MinDiscountPerLiter = request.MinDiscountPerLiter;
    if (request.MarginPercent is not null) fuel.MarginPercent = request.MarginPercent;
    if (request.PumpPricePerLiter is not null) fuel.PumpPricePerLiter = request.PumpPricePerLiter;

    // A fuel that has never been priced has no previous price, so this row is not a change but the
    // price it was born with - the same as every later row's role, and the reason a fuel's history
    // starts at its creation rather than at the first time somebody edited it.
    await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.Operator, GetUserId(), ct: ct);

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
            "Fuel", fuel.Id.ToString(), "FuelAdded",
            null, newValue, userId, GetUserName(),
            $"{Brand.Name} / {fuel.Name}: added at {(finalPerLiter ?? baseUahPerLiter):F2} UAH/L",
            Brand!.Slug,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(
            Brand.Name, fuel.Name, request.SupplierPricePerLiter, finalPerLiter ?? baseUahPerLiter,
            deliberate: true, ct);

        return Created($"/api/admin/brand-fuels/{fuel.Id}", new { id = fuel.Id });
    }

[HttpPut("{fuelId}")]
    public async Task<IActionResult> UpdateFuel([FromRoute] Guid fuelId, [FromBody] BrandFuelDto request, CancellationToken ct)
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

        // The price, or null when we cannot know it - see the create path above.
        var finalPerLiter = FuelPricing.FinalPerLiterOrNull(
            request.SupplierPricePerLiter, request.MarginUahPerLiter,
            request.PumpPricePerLiter, request.MinDiscountPerLiter);

        // Slice-3 hard block: reject a below-cost price unless this supplier+fuel is opted in.
        // A cost that is not stated is not below cost; it is unpriced, and it is saved as it is.
        var belowCost = request.SupplierPricePerLiter is { } knownCost
                        && request.MarginUahPerLiter is { } knownMargin
                        && FuelPricing.IsBelowCost(
                            knownCost, knownMargin, request.PumpPricePerLiter, request.MinDiscountPerLiter);
        if (belowCost && !request.AllowBelowCost)
            return BadRequest(new { code = BelowCostSaleBlockedException.Code, message = "Price is below supplier cost; enable the below-cost opt-in to save it." });

        // base = pump/reference (колонка) when known, else legacy final + min discount.
        var baseUahPerLiter = request.PumpPricePerLiter
                              ?? (finalPerLiter is { } known
                                  ? known + request.MinDiscountPerLiter
                                  : request.MinDiscountPerLiter);

        // There is no offer to find, create or flip. Flipping `isTestData` moves the SIZES to the
        // other pool, below; the price is the fuel's and there is nothing pool-shaped left to move.
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

        // Snapshot BEFORE anything is written below. The "before" is the fuel's own figures, which
        // is where every price figure lives now; there is no offer to fall back to for the old one.
        var oldSupplierPrice = fuel.SupplierPricePerLiter;
        var oldMarginPrice = fuel.MarginUahPerLiter;
        // audit payload records what the customer paid per litre, and that number is still a fact even
        // though nothing stores it.
        var oldFinalPrice = FuelPricing.FinalPerLiterOrNull(
            oldSupplierPrice,
            oldMarginPrice,
            fuel.PumpPricePerLiter,
            fuel.MinDiscountPerLiter);

        var oldValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            SupplierPricePerLiter = oldSupplierPrice,
            MarginUahPerLiter = oldMarginPrice,
            MarginPercent = fuel.MarginPercent,
            FinalPricePerLiter = oldFinalPrice,
            fuel.BasePrice,
            fuel.DiscountPrice
        });

        // Every knob lands on the fuel. There is one price per grade, so there is no second
        // place to write it and no second writer that could forget.
        fuel.SupplierPricePerLiter = request.SupplierPricePerLiter;
        fuel.MarginUahPerLiter = request.MarginUahPerLiter;
        fuel.MarginPercent = request.MarginPercent;
        fuel.PumpPricePerLiter = request.PumpPricePerLiter;
        fuel.MinDiscountPerLiter = request.MinDiscountPerLiter;
        fuel.UpdatedAtUtc = DateTime.UtcNow;
        // The loop that restated every volume's total after a reprice is gone, and its absence is the
        // improvement rather than a gap: a volume's price is now the offer's rate x its litres unless
        // somebody set an override, so there is nothing to restate - and an override is no longer erased
        // by the next pump import, which is what made this endpoint dangerous to touch.

        // The history row, before the save that persists the offer - and it also stamps
        // price_updated_at / price_updated_by_user_id, which this endpoint previously left at their
        // creation-time values, so the audit trail could not say when an operator repriced a fuel.
        await _priceRecorder.RecordAsync(fuel, PriceHistoryReasons.Operator, GetUserId(), ct: ct);

        // The other pool has nothing to archive: an offer existed to hold a price, and the
        // price is the fuel's. What separates the pools now is the size list, handled above.
        fuel.Name = request.Name;
        // base = pump/reference (колонка) when known, else legacy final + min discount;
        // discount = customer price (final).
        fuel.BasePrice = Math.Round(baseUahPerLiter, 2, MidpointRounding.AwayFromZero);
        // With no stated cost there is no final price, so the fuel is recorded at the column.
        fuel.DiscountPrice = Math.Round(finalPerLiter ?? baseUahPerLiter, 2, MidpointRounding.AwayFromZero);
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

                if (codeOwner != Guid.Empty)
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
            changes.Add($"final {oldFinalPrice:F2} → {(finalPerLiter ?? baseUahPerLiter):F2}");

        var summary = changes.Count > 0
            ? $"{BrandName} / {fuel.Name}: {string.Join(", ", changes)}"
            : $"{BrandName} / {fuel.Name}: updated";

        await _eventService.RecordEventAsync(
            "Fuel", fuelId.ToString(), "PriceChanged",
            oldValue, newValue, userId, GetUserName(),
            summary,
            Brand!.Slug,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(
                BrandName, fuel.Name, request.SupplierPricePerLiter, finalPerLiter ?? baseUahPerLiter,
                deliberate: true, ct);

        return Ok(new { success = true });
    }

    [HttpDelete("{fuelId}")]
    public async Task<IActionResult> DeleteFuel([FromRoute] Guid fuelId, CancellationToken ct)
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

        // Archive rather than remove - same reasoning as the brand branch above: the cascade from
        // fuel_types reaches fuel_vouchers (#217). The sizes are not archived either: they belong to
        // the fuel, and this row is the one that says no. It used to be a second row doing that job,
        // and a live offer put the fuel back on the shelf the moment a nominals edit touched it.
        var archivedAt = DateTime.UtcNow;

        fuel.IsDeleted = true;
        fuel.DeletedAtUtc = archivedAt;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Fuel", fuelId.ToString(), "FuelRemoved",
            JsonSerializer.Serialize(new { fuelName, BrandName }), "{}",
            userId, GetUserName(),
            $"{BrandName} / {fuelName}: removed",
            Brand!.Slug,
            ct);

        return Ok(new { success = true });
    }

    [HttpPut("brands/{id}/nominals")]
    public async Task<IActionResult> UpdateNominals([FromRoute] Guid id, [FromBody] List<int> nominals, CancellationToken ct)
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

// The sizes a fuel is sold in are the operator's answer, written once per fuel and pool. The
        // volume loops that used to follow are gone: `fuel_offer_volumes` was a second copy of this
        // same answer, and keeping both in step was the whole cost of having it.
        foreach (var fuel in BrandFuels)
        {
            foreach (var leaving in existingNominals.Where(n => n.FuelTypeId == fuel.Id
                                                             && n.DataKind == DataKinds.Real
                                                             && !wanted.Contains((int)n.Liters)))
            {
                _context.FuelNominals.Remove(leaving);
            }

            foreach (var liters in wanted.Where(l => existingNominals.All(
                         n => n.FuelTypeId != fuel.Id || n.DataKind != DataKinds.Real || n.Liters != l)))
            {
                // The brand screen edits the live catalogue. The rehearsal one is seeded by the QA
                // scripts, exactly as it was before the sizes moved onto the fuel.
                _context.FuelNominals.Add(new FuelNominal
                {
                    FuelTypeId = fuel.Id,
                    DataKind = DataKinds.Real,
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
            "Nominal", id.ToString(), "NominalSetChanged",
            oldValue, newValue, userId, GetUserName(),
            $"{Brand.Name}: nominals changed [{string.Join(", ", nominals.OrderBy(n => n))}]",
            Brand.Slug,
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

