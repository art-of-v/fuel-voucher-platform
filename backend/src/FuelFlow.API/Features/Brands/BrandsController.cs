using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Features.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using FuelFlow.Features.Stations;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/brands")]
[Authorize(Policy = "Staff")]
public sealed class BrandsController : ControllerBase
{
private readonly GetBrandsQueryHandler _getAll;
    private readonly GetBrandByIdQueryHandler _getById;
    private readonly GetBrandHistoryQueryHandler _getHistory;
    private readonly GetFuelPriceHistoryQueryHandler _getPriceHistory;
    private readonly GetFuelPriceAtQueryHandler _getPriceAt;
    private readonly AuditEventService _eventService;
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;

    // Every price this controller writes goes through here, so the history cannot be missed by the one
    // endpoint that forgets. See OfferPriceRecorder for why that is a service rather than a comment.
    private readonly OfferPriceRecorder _priceRecorder;

    public BrandsController(
        GetBrandsQueryHandler getAll,
        GetBrandByIdQueryHandler getById,
        GetBrandHistoryQueryHandler getHistory,
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AuditEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        OfferPriceRecorder priceRecorder)
    {
        _getAll = getAll;
        _getById = getById;
        _getHistory = getHistory;
        _getPriceHistory = getPriceHistory;
        _getPriceAt = getPriceAt;
        _eventService = eventService;
        _context = context;
        _notifications = notifications;
        _priceRecorder = priceRecorder;
    }

    private static readonly List<int> DefaultNominals = new() { 2, 3, 5, 10, 20, 50 };

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetBrandsQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetBrandByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getHistory.HandleAsync(new GetBrandHistoryQuery(id, limit), ct));

    /// <summary>
    /// What one fuel was priced at, oldest first. The numbers the dashboard and reconciliation apply to
    /// historical orders, against the sequence that produced them - see
    /// <see cref="GetFuelPriceHistoryQueryHandler"/>.
    /// </summary>
    [HttpGet("fuels/{fuelId}/price-history")]
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
    [HttpGet("fuels/{fuelId}/price-at")]
    public async Task<IActionResult> GetFuelPriceAt(
        [FromRoute] string fuelId,
        [FromQuery] DateTime asOf,
        CancellationToken ct = default) =>
        Ok(await _getPriceAt.HandleAsync(new GetFuelPriceAtQuery(fuelId, asOf), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BrandDto request, CancellationToken ct)
    {
        var exists = await _context.Stations.AnyAsync(s => s.Id == request.Id, ct);
        if (exists)
            return Conflict(new { error = "Brand with this ID already exists" });

        var station = new Station
        {
            Id = request.Id,
            Name = request.Name,
            LogoText = request.LogoText,
            Color = request.Color,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.Stations.Add(station);
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { station.Id, station.Name });
        await _eventService.RecordEventAsync(
            "Brand", station.Id, "BrandCreated",
            null, newValue, userId, GetUserName(),
            $"Created brand {station.Name}",
            station.Id,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = station.Id }, station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] BrandDto request, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var oldName = station.Name;
        var oldLogoText = station.LogoText;
        var oldColor = station.Color;
        var oldSortOrder = station.SortOrder;
        var oldValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color, station.SortOrder });

        station.Name = request.Name;
        station.LogoText = request.LogoText;
        station.Color = request.Color;
        station.SortOrder = request.SortOrder;
        station.UpdatedAtUtc = DateTime.UtcNow;
        _context.Stations.Update(station);
        await _context.SaveChangesAsync(ct);

        var changes = new List<string>();
        if (oldName != request.Name)
            changes.Add($"name {oldName}  {request.Name}");
        if (oldLogoText != request.LogoText)
            changes.Add($"logo {oldLogoText}  {request.LogoText}");
        if (oldColor != request.Color)
            changes.Add($"color {oldColor}  {request.Color}");
        if (oldSortOrder != request.SortOrder)
            changes.Add($"sort order {oldSortOrder}  {request.SortOrder}");

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color, station.SortOrder });
        var summary = changes.Count > 0
            ? $"{station.Name}: {string.Join(", ", changes)}"
            : $"Updated brand {station.Name}";
        await _eventService.RecordEventAsync(
            "Brand", id, "BrandUpdated",
            oldValue, newValue, userId, GetUserName(),
            summary,
            id,
            ct);

        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var name = station.Name;
        var fuelTypes = await _context.FuelTypes.Where(f => f.StationId == id).ToListAsync(ct);
        var fuelIds = fuelTypes.Select(f => f.Id).ToList();
        // Archive-the-offers view of the same question as the brand list above: this deletion path needs
        // the rows to mutate, not the read projection the card is built from.
        var offerIdsToArchive = await _context.FuelOffers
            .Where(o => fuelIds.Contains(o.FuelTypeId))
            .Select(o => o.Id).ToListAsync(ct);
        var packages = await _context.FuelOfferVolumes
            .Where(v => offerIdsToArchive.Contains(v.OfferId))
            .ToListAsync(ct);
        var offersToArchive = await _context.FuelOffers
            .Where(o => offerIdsToArchive.Contains(o.Id))
            .ToListAsync(ct);

        // Archive the whole branch instead of removing it. fuel_vouchers.fuel_type_id CASCADEs from
        // here, so the old RemoveRange cascade could take paid-for vouchers with it; #217 keeps
        // anything that affects history. The !IsDeleted filters keep all three out of the catalog.
        var archivedAt = DateTime.UtcNow;
        foreach (var package in packages)
        {
            package.IsDeleted = true;
            package.DeletedAtUtc = archivedAt;
        }
        // The offers go too. Archiving only their volumes would leave a live offer behind with no
        // sellable volume, and the next nominals edit would happily add volumes back to a brand that was
        // deleted - the offer is the row that says "this fuel is on sale here", so it is the row that has
        // to say no.
        foreach (var offer in offersToArchive)
        {
            offer.IsDeleted = true;
            offer.DeletedAtUtc = archivedAt;
        }

        foreach (var fuelType in fuelTypes)
        {
            fuelType.IsDeleted = true;
            fuelType.DeletedAtUtc = archivedAt;
        }
        station.IsDeleted = true;
        station.DeletedAtUtc = archivedAt;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Brand", id, "BrandDeleted",
            JsonSerializer.Serialize(new { name }), "{}", userId, GetUserName(),
            $"Deleted brand {name}",
            id,
            ct);

        return Ok(new { success = true });
    }

    [HttpPost("{id}/fuels")]
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

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        // One fuel type per name per station, refused rather than accepted as a second row. This fuel
        // id is generated here, so nothing about the write collides on a duplicate the way it did when
        // the id was the brand-encoded 'okko-95' - and a twin is not a cosmetic duplicate: it becomes a
        // second offer, the storefront lists one fuel at two prices, and the voucher importer resolves
        // the fuel by name with FirstOrDefault (OkkoFuelClassifier/WogFuelClassifier), so the twin
        // without the imported pump price is the one a real voucher can land on. The database refuses
        // the same thing with ux_fuel_types_station_name; this says which fuel already owns the name.
        var duplicate = await _context.FuelTypes
            .Where(f => f.StationId == id && f.Name == request.Name)
            .Select(f => f.Id)
            .FirstOrDefaultAsync(ct);

        if (duplicate is not null)
        {
            return Conflict(new
            {
                message = $"'{request.Name}' already exists at {station.Name}. Price it with PUT "
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
                .Where(f => f.StationId == id && f.BrandFuelCode == brandFuelCode)
                .Select(f => f.Id)
                .FirstOrDefaultAsync(ct);

            if (codeOwner is not null)
            {
                return Conflict(new
                {
                    message = $"Brand fuel code '{brandFuelCode}' is already taken at {station.Name} by fuel "
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
            StationId = id,
            Name = request.Name,
            BrandFuelCode = brandFuelCode,
            // base_price / discount_price are UAH per liter, decimal to the kopeck (spec §12, #90):
            // round to 2dp, never store kopecks-as-integer (that once made the app show 8492.00).
            BasePrice = Math.Round(baseUahPerLiter, 2, MidpointRounding.AwayFromZero),
            DiscountPrice = Math.Round(finalPerLiter, 2, MidpointRounding.AwayFromZero),
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
                .Where(f => f.StationId == id && f.Id != fuel.Id)
                .Select(f => f.Id).ToListAsync(ct);
            litersToCreate = await _context.FuelOfferVolumes
                .Where(v => existingFuelIds.Contains(v.Offer.FuelTypeId))
                .Select(v => (int)v.Liters)
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
        // the same fuel at the same station have one price per litre and therefore one offer. The old
        // row-per-volume shape wrote that same price N times and could not stop the copies drifting -
        // which is what the backfill had to reconcile when the split landed.
        var pool = DataKinds.ForPool(request.IsTestData.Value);
        var offer = new FuelOffer
        {
            Id = OfferKeys.For(fuel.Id, pool),
            StationId = id,
            FuelTypeId = fuel.Id,
            SupplierPricePerLiter = request.SupplierPricePerLiter,
            MarginUahPerLiter = request.MarginUahPerLiter,
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
            _context.FuelOfferVolumes.Add(new FuelOfferVolume
            {
                Id = Guid.CreateVersion7().ToString(),
                OfferId = offer.Id,
                Liters = liters,
                // No price override: a fuel added through this screen is priced like itself, and the
                // "before" price the storefront strikes through is recomputed from the pump price via
                // FuelPricing.OriginalPackagePrice (planning #73), so the supplier cost never leaks to
                // anonymous callers. An operator who wants one size priced by hand sets an override
                // afterwards, and from then on it survives every reprice — which is what made this
                // column worth having.
                PriceOverrideUah = null,
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
            $"{station.Name} / {fuel.Name}: added at {finalPerLiter:F2} UAH/L",
            id,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(station.Name, fuel.Name, request.SupplierPricePerLiter, finalPerLiter, deliberate: true, ct);

        return CreatedAtAction(nameof(GetById), new { id }, new { id = fuel.Id });
    }

[HttpPut("fuels/{fuelId}")]
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
        // removes. `data_kind` is part of the offer's identity - UNIQUE (station_id, fuel_type_id,
        // data_kind) - so the same fuel at the same station has TWO offers, one per pool, and they are
        // different rows rather than one row with a flag. Moving a fuel from real to rehearsal
        // therefore means moving its volumes onto the rehearsal offer and archiving the real one.
        var otherPool = pool == DataKinds.Real ? DataKinds.Qa : DataKinds.Real;
        var otherOfferId = OfferKeys.For(fuel.Id, otherPool);
        var otherOffer = await _context.FuelOffers.FirstOrDefaultAsync(o => o.Id == otherOfferId, ct);

        if (offer is null)
        {
            offer = new FuelOffer
            {
                Id = offerId,
                StationId = fuel.StationId,
                FuelTypeId = fuel.Id,
                DataKind = pool,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _context.FuelOffers.Add(offer);
        }

        var volumes = await _context.FuelOfferVolumes
            .Where(v => v.OfferId == offerId || v.OfferId == otherOfferId)
            .ToListAsync(ct);

        var seededVolumes = volumes.Count == 0;
        if (seededVolumes)
        {
            volumes = DefaultNominals.Select(liters => new FuelOfferVolume
            {
                Id = Guid.CreateVersion7().ToString(),
                OfferId = offerId,
                Liters = liters,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }).ToList();
            _context.FuelOfferVolumes.AddRange(volumes);
        }
        else if (offerId != otherOfferId)
        {
            // Re-keying: the volumes belonged to the other pool's offer and now belong to this one.
            foreach (var volume in volumes) volume.OfferId = offerId;
        }

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == fuel.StationId, ct);
        var stationName = station?.Name ?? "?";

        // Snapshot BEFORE anything is written below. Reading "old" from `otherOffer` looks right when the
        // edit is flipping a fuel between pools - the other pool's offer is what it had - and silently
        // records nothing at all otherwise, because on an ordinary reprice `otherOffer` is null. The
        // audit then reads "supplier  -> 100.00" and the operator has no record of what they changed.
        var previousOffer = offer ?? otherOffer;
        var oldSupplierPrice = previousOffer?.SupplierPricePerLiter;
        var oldMarginPrice = previousOffer?.MarginUahPerLiter;
        // Computed from the offer's own knobs rather than read from a column: the audit payload records
        // what the customer paid per litre, and that number is still a fact even though nothing stores it.
        var oldFinalPrice = previousOffer?.ComputedFinalPricePerLiter;
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
        // ...and it is mirrored onto the offer in the same breath, because the storefront, checkout,
        // the dashboard and the renewal guard all read it from there and none of them changes in this
        // step. ONE writer sets both, so the copy cannot drift; dropping the offer's column is the
        // following migration (planning #360), and until then this line is the only thing keeping the
        // two in agreement.
        offer.MarginUahPerLiter = request.MarginUahPerLiter;
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
                    .Where(f => f.StationId == fuel.StationId && f.BrandFuelCode == wanted && f.Id != fuel.Id)
                    .Select(f => f.Id)
                    .FirstOrDefaultAsync(ct);

                if (codeOwner is not null)
                {
                    return Conflict(new
                    {
                        message = $"Brand fuel code '{wanted}' is already taken at {fuel.StationId} by fuel "
                                  + $"{codeOwner}. A code identifies one fuel per network.",
                        fuelTypeId = codeOwner,
                        field = nameof(request.BrandFuelCode)
                    });
                }
            }

            fuel.BrandFuelCode = wanted;
        }

        if (!seededVolumes) _context.FuelOfferVolumes.UpdateRange(volumes);
        if (!seededVolumes) _context.FuelOffers.Update(offer);
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
            ? $"{stationName} / {fuel.Name}: {string.Join(", ", changes)}"
            : $"{stationName} / {fuel.Name}: updated";

        await _eventService.RecordEventAsync(
            "Fuel", fuelId, "PriceChanged",
            oldValue, newValue, userId, GetUserName(),
            summary,
            fuel.StationId,
            ct);

        // Record the deliberate loss-leader decision (we only reach here below-cost when opted in).
        if (belowCost)
            await _notifications.BelowCostAsync(stationName, fuel.Name, request.SupplierPricePerLiter, finalPerLiter, deliberate: true, ct);

        return Ok(new { success = true });
    }

    [HttpDelete("fuels/{fuelId}")]
    public async Task<IActionResult> DeleteFuel([FromRoute] string fuelId, CancellationToken ct)
    {
        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId, ct);
        if (fuel is null) return NotFound();

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == fuel.StationId, ct);
        var stationName = station?.Name ?? "?";
        var fuelName = fuel.Name;

        // Same as the brand branch: this path archives rows rather than reading them.
        var offerIdsToArchive = await _context.FuelOffers
            .Where(o => o.FuelTypeId == fuelId)
            .Select(o => o.Id).ToListAsync(ct);
        var packages = await _context.FuelOfferVolumes
            .Where(v => offerIdsToArchive.Contains(v.OfferId))
            .ToListAsync(ct);
        var offersToArchive = await _context.FuelOffers
            .Where(o => offerIdsToArchive.Contains(o.Id))
            .ToListAsync(ct);

        // Archive rather than remove - same reasoning as the brand branch above: the cascade from
        // fuel_types reaches fuel_vouchers (#217).
        var archivedAt = DateTime.UtcNow;
        foreach (var package in packages)
        {
            package.IsDeleted = true;
            package.DeletedAtUtc = archivedAt;
        }
        // The offers go too. Archiving only their volumes would leave a live offer behind with no
        // sellable volume, and the next nominals edit would happily add volumes back to a brand that was
        // deleted - the offer is the row that says "this fuel is on sale here", so it is the row that has
        // to say no.
        foreach (var offer in offersToArchive)
        {
            offer.IsDeleted = true;
            offer.DeletedAtUtc = archivedAt;
        }        fuel.IsDeleted = true;
        fuel.DeletedAtUtc = archivedAt;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Fuel", fuelId, "FuelRemoved",
            JsonSerializer.Serialize(new { fuelName, stationName }), "{}",
            userId, GetUserName(),
            $"{stationName} / {fuelName}: removed",
            fuel.StationId,
            ct);

        return Ok(new { success = true });
    }

    [HttpPut("{id}/nominals")]
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

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var stationFuels = await _context.FuelTypes.Where(f => f.StationId == id).ToListAsync(ct);
        var fuelIds = stationFuels.Select(f => f.Id).ToList();
        // AsTracking: the volumes below are updated in place now, and the context defaults to
        // NoTracking - without it the edit would persist nothing (NoTrackingMutationGuardTests).
        //
        // Offers are tracked alongside volumes because the per-litre knobs this used to read off "the
        // fuel's first existing package" are the offer's, once. Reading them per package meant N copies
        // of one price, and the copy that happened to sort first was the one the whole fuel was
        // repriced from.
        var offers = await _context.FuelOffers
            .AsTracking()
            .Where(o => fuelIds.Contains(o.FuelTypeId))
            .ToListAsync(ct);

        var offerIds = offers.Select(o => o.Id).ToList();
        var existingVolumes = await _context.FuelOfferVolumes
            .AsTracking()
            .Where(v => offerIds.Contains(v.OfferId))
            .ToListAsync(ct);

        var oldValue = JsonSerializer.Serialize(
            existingVolumes.Where(v => !v.IsDeleted).Select(v => (int)v.Liters).Distinct().OrderBy(l => l).ToList());

        // #217: keep what survives, archive what leaves, add what is new.
        //
        // This used to RemoveRange every package and recreate the whole set with fresh ids. Two
        // things were wrong with that: order_line_items point at packages, so removing one took the
        // record of what a customer bought with it, and a fresh id on every price edit meant the
        // package an order referenced was never the package in the catalog. Reusing a package that
        // stays keeps its id, so historical rows keep resolving to the thing they bought.
        //
        // Pricing is unchanged: the per-litre knobs come from the fuel's offer, and only the derived
        // package total is rewritten on each volume.
        var wanted = nominals.Distinct().ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var leaving in existingVolumes.Where(v => !wanted.Contains((int)v.Liters)))
        {
            leaving.IsDeleted = true;
            leaving.DeletedAtUtc = now;
        }

        foreach (var fuel in stationFuels)
        {
            // A fuel can own an offer per pool now rather than one flagged set of packages, so the
            // nominals are applied to each of its offers: both catalogues should offer the same sizes.
            foreach (var offer in offers.Where(o => o.FuelTypeId == fuel.Id))
            {
                var supplierPrice = offer.SupplierPricePerLiter ?? 0;
                var marginUah = offer.MarginUahPerLiter ?? 0;
                var finalPrice = FuelPricing.FinalPerLiter(
                    supplierPrice, marginUah, offer.PumpPricePerLiter, offer.MinDiscountPerLiter ?? 0);

                foreach (var liters in wanted)
                {
                    var kept = existingVolumes.FirstOrDefault(
                        v => v.OfferId == offer.Id && (int)v.Liters == liters);

                    if (kept is not null)
                    {
                        // Same row, same id, same CreatedAtUtc, and the same price: a kept nominal's
                        // price follows its offer, so there is nothing to refresh. If it carries an override
                        // that override survives, which is the one thing this endpoint used to undo.
                        kept.UpdatedAtUtc = now;
                        continue;
                    }

                    _context.FuelOfferVolumes.Add(new FuelOfferVolume
                    {
                        Id = Guid.CreateVersion7().ToString(),
                        OfferId = offer.Id,
                        Liters = liters,
                        PriceOverrideUah = null,   // priced from the offer like every other nominal
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    });
                }
            }
        }
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(nominals.OrderBy(n => n).ToList());
        await _eventService.RecordEventAsync(
            "Nominal", id, "NominalSetChanged",
            oldValue, newValue, userId, GetUserName(),
            $"{station.Name}: nominals changed [{string.Join(", ", nominals.OrderBy(n => n))}]",
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
