using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers;

[ApiController]
[Route("api/admin/providers")]
[Authorize(Policy = "Staff")]
public sealed class ProvidersController : ControllerBase
{
    private readonly GetProvidersQueryHandler _getAll;
    private readonly GetProviderByIdQueryHandler _getById;
    private readonly GetProviderHistoryQueryHandler _getHistory;
    private readonly ProviderEventService _eventService;
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;

    public ProvidersController(
        GetProvidersQueryHandler getAll,
        GetProviderByIdQueryHandler getById,
        GetProviderHistoryQueryHandler getHistory,
        ProviderEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications)
    {
        _getAll = getAll;
        _getById = getById;
        _getHistory = getHistory;
        _eventService = eventService;
        _context = context;
        _notifications = notifications;
    }

    private static readonly List<int> DefaultNominals = new() { 2, 3, 5, 10, 20, 50 };

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetProvidersQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetProviderByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getHistory.HandleAsync(new GetProviderHistoryQuery(id, limit), ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProviderDto request, CancellationToken ct)
    {
        var exists = await _context.Stations.AnyAsync(s => s.Id == request.Id, ct);
        if (exists)
            return Conflict(new { error = "Provider with this ID already exists" });

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
            "Provider", station.Id, "ProviderCreated",
            null, newValue, userId, GetUserName(),
            $"Created provider {station.Name}",
            station.Id,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = station.Id }, station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] ProviderDto request, CancellationToken ct)
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
            : $"Updated provider {station.Name}";
        await _eventService.RecordEventAsync(
            "Provider", id, "ProviderUpdated",
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
        var packages = await _context.FuelPackages.Where(p => fuelIds.Contains(p.FuelTypeId)).ToListAsync(ct);

        _context.FuelPackages.RemoveRange(packages);
        _context.FuelTypes.RemoveRange(fuelTypes);
        _context.Stations.Remove(station);
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Provider", id, "ProviderDeleted",
            JsonSerializer.Serialize(new { name }), "{}", userId, GetUserName(),
            $"Deleted provider {name}",
            id,
            ct);

        return Ok(new { success = true });
    }

    [HttpPost("{id}/fuels")]
    public async Task<IActionResult> AddFuel([FromRoute] string id, [FromBody] CreateFuelRequest request, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

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
            Id = Guid.NewGuid().ToString(),
            StationId = id,
            Name = request.Name,
            // base_price / discount_price are UAH per liter (seed data and all readers
            // treat them as such); storing kopecks here made the mobile app show 8492.00.
            BasePrice = (int)Math.Round(baseUahPerLiter),
            DiscountPrice = (int)Math.Round(finalPerLiter),
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
            litersToCreate = await _context.FuelPackages
                .Where(p => existingFuelIds.Contains(p.FuelTypeId))
                .Select(p => (int)p.Liters)
                .Distinct()
                .OrderBy(l => l)
                .ToListAsync(ct);
        }

        if (litersToCreate.Count == 0)
        {
            litersToCreate = DefaultNominals;
        }

        foreach (var liters in litersToCreate)
        {
            _context.FuelPackages.Add(new FuelPackage
            {
                Id = Guid.NewGuid().ToString(),
                StationId = id,
                FuelTypeId = fuel.Id,
                FuelName = fuel.Name,
                Liters = liters,
                Price = (int)Math.Round(finalPerLiter * liters),
                // Public "before" price = pump × liters (falls back to the sale price when no pump);
                // never the supplier cost, which must not leak to anonymous callers (planning #73).
                OriginalPrice = FuelPricing.OriginalPackagePrice(request.PumpPricePerLiter, liters, (int)Math.Round(finalPerLiter * liters)),
                SupplierPricePerLiter = request.SupplierPricePerLiter,
                MarginUahPerLiter = request.MarginUahPerLiter,
                MarginPercent = request.MarginPercent,
                FinalPricePerLiter = finalPerLiter,
                PumpPricePerLiter = request.PumpPricePerLiter,
                MinDiscountPerLiter = request.MinDiscountPerLiter,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
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
    public async Task<IActionResult> UpdateFuel([FromRoute] string fuelId, [FromBody] ProviderFuelDto request, CancellationToken ct)
    {
        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId, ct);
        if (fuel is null) return NotFound();

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == fuelId).ToListAsync(ct);
        var seededPackages = packages.Count == 0;

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

        if (seededPackages)
        {
            foreach (var liters in DefaultNominals)
            {
                var pkg = new FuelPackage
                {
                    Id = Guid.NewGuid().ToString(),
                    StationId = fuel.StationId,
                    FuelTypeId = fuel.Id,
                    FuelName = fuel.Name,
                    Liters = liters,
                    Price = (int)Math.Round(finalPerLiter * liters),
                    OriginalPrice = FuelPricing.OriginalPackagePrice(request.PumpPricePerLiter, liters, (int)Math.Round(finalPerLiter * liters)),
                    SupplierPricePerLiter = request.SupplierPricePerLiter,
                    MarginUahPerLiter = request.MarginUahPerLiter,
                    MarginPercent = request.MarginPercent,
                    FinalPricePerLiter = finalPerLiter,
                    PumpPricePerLiter = request.PumpPricePerLiter,
                    MinDiscountPerLiter = request.MinDiscountPerLiter,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _context.FuelPackages.Add(pkg);
                packages.Add(pkg);
            }
        }

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == fuel.StationId, ct);
        var stationName = station?.Name ?? "?";

        var firstPkg = packages.First();
        var oldFuelName = fuel.Name;
        var oldSupplierPrice = firstPkg.SupplierPricePerLiter;
        var oldMarginPrice = firstPkg.MarginUahPerLiter;
        var oldFinalPrice = firstPkg.FinalPricePerLiter;
        var oldValue = JsonSerializer.Serialize(new
        {
            fuel.Name,
            SupplierPricePerLiter = oldSupplierPrice,
            MarginUahPerLiter = oldMarginPrice,
            firstPkg.MarginPercent,
            FinalPricePerLiter = oldFinalPrice,
            fuel.BasePrice,
            fuel.DiscountPrice
        });

        foreach (var pkg in packages)
        {
            pkg.FuelName = request.Name;
            pkg.SupplierPricePerLiter = request.SupplierPricePerLiter;
            pkg.MarginUahPerLiter = request.MarginUahPerLiter;
            pkg.MarginPercent = request.MarginPercent;
            pkg.PumpPricePerLiter = request.PumpPricePerLiter;
            pkg.MinDiscountPerLiter = request.MinDiscountPerLiter;
            pkg.FinalPricePerLiter = finalPerLiter;
            pkg.Price = (int)Math.Round(finalPerLiter * pkg.Liters);
            pkg.OriginalPrice = FuelPricing.OriginalPackagePrice(request.PumpPricePerLiter, pkg.Liters, (int)Math.Round(finalPerLiter * pkg.Liters));
            pkg.UpdatedAtUtc = DateTime.UtcNow;
        }

        fuel.Name = request.Name;
        // base = pump/reference (колонка) when known, else legacy final + min discount;
        // discount = customer price (final).
        fuel.BasePrice = (int)Math.Round(baseUahPerLiter);
        fuel.DiscountPrice = (int)Math.Round(finalPerLiter);
        fuel.AllowBelowCost = request.AllowBelowCost;
        fuel.UpdatedAtUtc = DateTime.UtcNow;

        if (!seededPackages)
        {
            _context.FuelPackages.UpdateRange(packages);
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

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == fuelId).ToListAsync(ct);
        _context.FuelPackages.RemoveRange(packages);
        _context.FuelTypes.Remove(fuel);
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
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var stationFuels = await _context.FuelTypes.Where(f => f.StationId == id).ToListAsync(ct);
        var fuelIds = stationFuels.Select(f => f.Id).ToList();
        var existingPackages = await _context.FuelPackages.Where(p => fuelIds.Contains(p.FuelTypeId)).ToListAsync(ct);

        var oldValue = JsonSerializer.Serialize(
            existingPackages.Select(p => (int)p.Liters).Distinct().OrderBy(l => l).ToList());

        _context.FuelPackages.RemoveRange(existingPackages);

        foreach (var fuel in stationFuels)
        {
            var pkg = existingPackages.FirstOrDefault(p => p.FuelTypeId == fuel.Id);
            var supplierPrice = pkg?.SupplierPricePerLiter ?? 0;
            var marginUah = pkg?.MarginUahPerLiter ?? 0;
            var marginPct = pkg?.MarginPercent;
            var pumpPrice = pkg?.PumpPricePerLiter;
            var minDiscount = pkg?.MinDiscountPerLiter ?? 0;
            var finalPrice = pkg is null
                ? 0m
                : FuelPricing.FinalPerLiter(supplierPrice, marginUah, pumpPrice, minDiscount);

            foreach (var liters in nominals)
            {
                _context.FuelPackages.Add(new FuelPackage
                {
                    Id = Guid.NewGuid().ToString(),
                    StationId = id,
                    FuelTypeId = fuel.Id,
                    FuelName = fuel.Name,
                    Liters = liters,
                    Price = (int)Math.Round(finalPrice * liters),
                    OriginalPrice = FuelPricing.OriginalPackagePrice(pumpPrice, liters, (int)Math.Round(finalPrice * liters)),
                    SupplierPricePerLiter = supplierPrice,
                    MarginUahPerLiter = marginUah,
                    MarginPercent = marginPct,
                    FinalPricePerLiter = finalPrice,
                    PumpPricePerLiter = pumpPrice,
                    MinDiscountPerLiter = minDiscount,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
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
