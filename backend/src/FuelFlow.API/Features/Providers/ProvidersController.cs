using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers;

[ApiController]
[Route("api/admin/providers")]
[Authorize(Roles = "Admin")]
public sealed class ProvidersController : ControllerBase
{
    private readonly GetProvidersQueryHandler _getAll;
    private readonly GetProviderByIdQueryHandler _getById;
    private readonly GetProviderHistoryQueryHandler _getHistory;
    private readonly ProviderEventService _eventService;
    private readonly ApplicationDbContext _context;

    public ProvidersController(
        GetProvidersQueryHandler getAll,
        GetProviderByIdQueryHandler getById,
        GetProviderHistoryQueryHandler getHistory,
        ProviderEventService eventService,
        ApplicationDbContext context)
    {
        _getAll = getAll;
        _getById = getById;
        _getHistory = getHistory;
        _eventService = eventService;
        _context = context;
    }

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
            ct);

        return CreatedAtAction(nameof(GetById), new { id = station.Id }, station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] ProviderDto request, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var oldValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color });

        station.Name = request.Name;
        station.LogoText = request.LogoText;
        station.Color = request.Color;
        station.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color });
        await _eventService.RecordEventAsync(
            "Provider", id, "ProviderUpdated",
            oldValue, newValue, userId, GetUserName(),
            $"Updated provider {station.Name}",
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
            ct);

        return Ok(new { success = true });
    }

    [HttpPost("{id}/fuels")]
    public async Task<IActionResult> AddFuel([FromRoute] string id, [FromBody] ProviderFuelDto request, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var fuel = new FuelTypeEntity
        {
            Id = Guid.NewGuid().ToString(),
            StationId = id,
            Name = request.Name,
            BasePrice = (int)Math.Round(request.FinalPricePerLiter * 100),
            DiscountPrice = (int)Math.Round(request.FinalPricePerLiter * 100),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.FuelTypes.Add(fuel);
        await _context.SaveChangesAsync(ct);

        foreach (var liters in request.PackageLiters)
        {
            _context.FuelPackages.Add(new FuelPackage
            {
                Id = Guid.NewGuid().ToString(),
                StationId = id,
                FuelTypeId = fuel.Id,
                FuelName = fuel.Name,
                Liters = liters,
                Price = (int)Math.Round(request.FinalPricePerLiter * liters),
                OriginalPrice = (int)Math.Round(request.SupplierPricePerLiter * liters),
                SupplierPricePerLiter = request.SupplierPricePerLiter,
                MarginUahPerLiter = request.MarginUahPerLiter,
                MarginPercent = request.MarginPercent,
                FinalPricePerLiter = request.FinalPricePerLiter,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { fuel.Id, fuel.Name, request.SupplierPricePerLiter, request.MarginUahPerLiter, request.FinalPricePerLiter });
        await _eventService.RecordEventAsync(
            "Fuel", fuel.Id, "FuelAdded",
            null, newValue, userId, GetUserName(),
            $"{station.Name} / {fuel.Name}: added at {request.FinalPricePerLiter:F2} UAH/L",
            ct);

        return CreatedAtAction(nameof(GetById), new { id }, new { id = fuel.Id });
    }

    [HttpPut("fuels/{fuelId}")]
    public async Task<IActionResult> UpdateFuel([FromRoute] string fuelId, [FromBody] ProviderFuelDto request, CancellationToken ct)
    {
        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == fuelId).ToListAsync(ct);
        if (packages.Count == 0) return NotFound();

        var fuel = await _context.FuelTypes.FirstOrDefaultAsync(f => f.Id == fuelId, ct);
        if (fuel is null) return NotFound();

        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == fuel.StationId, ct);
        var stationName = station?.Name ?? "?";

        var firstPkg = packages.First();
        var oldValue = JsonSerializer.Serialize(new
        {
            firstPkg.SupplierPricePerLiter,
            firstPkg.MarginUahPerLiter,
            firstPkg.MarginPercent,
            firstPkg.FinalPricePerLiter
        });

        foreach (var pkg in packages)
        {
            pkg.SupplierPricePerLiter = request.SupplierPricePerLiter;
            pkg.MarginUahPerLiter = request.MarginUahPerLiter;
            pkg.MarginPercent = request.MarginPercent;
            pkg.FinalPricePerLiter = request.FinalPricePerLiter;
            pkg.Price = (int)Math.Round(request.FinalPricePerLiter * pkg.Liters);
            pkg.OriginalPrice = (int)Math.Round(request.SupplierPricePerLiter * pkg.Liters);
            pkg.UpdatedAtUtc = DateTime.UtcNow;
        }

        fuel.BasePrice = (int)Math.Round(request.FinalPricePerLiter * 100);
        fuel.DiscountPrice = (int)Math.Round(request.FinalPricePerLiter * 100);
        fuel.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new
        {
            request.SupplierPricePerLiter,
            request.MarginUahPerLiter,
            request.MarginPercent,
            request.FinalPricePerLiter
        });

        var changes = new List<string>();
        if (firstPkg.SupplierPricePerLiter != request.SupplierPricePerLiter)
            changes.Add($"supplier {firstPkg.SupplierPricePerLiter:F2} → {request.SupplierPricePerLiter:F2}");
        if (firstPkg.MarginUahPerLiter != request.MarginUahPerLiter)
            changes.Add($"margin {firstPkg.MarginUahPerLiter:F2} → {request.MarginUahPerLiter:F2}");
        if (firstPkg.FinalPricePerLiter != request.FinalPricePerLiter)
            changes.Add($"final {firstPkg.FinalPricePerLiter:F2} → {request.FinalPricePerLiter:F2}");

        var summary = changes.Count > 0
            ? $"{stationName} / {fuel.Name}: {string.Join(", ", changes)}"
            : $"{stationName} / {fuel.Name}: updated";

        await _eventService.RecordEventAsync(
            "Fuel", fuelId, "PriceChanged",
            oldValue, newValue, userId, GetUserName(),
            summary,
            ct);

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
            var finalPrice = pkg?.FinalPricePerLiter ?? 0;
            var supplierPrice = pkg?.SupplierPricePerLiter ?? 0;
            var marginUah = pkg?.MarginUahPerLiter ?? 0;
            var marginPct = pkg?.MarginPercent;

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
                    OriginalPrice = (int)Math.Round(supplierPrice * liters),
                    SupplierPricePerLiter = supplierPrice,
                    MarginUahPerLiter = marginUah,
                    MarginPercent = marginPct,
                    FinalPricePerLiter = finalPrice,
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
            ct);

        return Ok(new { success = true });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName() =>
        User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst("name")?.Value;
}
