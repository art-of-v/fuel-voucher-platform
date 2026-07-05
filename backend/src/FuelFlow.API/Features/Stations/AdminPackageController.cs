using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/packages")]
[Authorize(Roles = "Admin")]
public sealed class AdminPackageController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminPackageController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.FuelPackages
            .AsNoTracking()
            .OrderBy(x => x.StationId)
            .ThenBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("station/{stationId}")]
    public async Task<IActionResult> GetByStation([FromRoute] string stationId, CancellationToken cancellationToken)
    {
        var items = await _dbContext.FuelPackages
            .AsNoTracking()
            .Where(x => x.StationId == stationId)
            .OrderBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions(CancellationToken cancellationToken)
    {
        var allVouchers = await _dbContext.FuelVouchers
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var uniqueCombos = allVouchers
            .GroupBy(v => new { v.Provider, v.FuelTypeId, v.Liters })
            .Select(g => g.First())
            .ToList();

        var allStations = await _dbContext.Stations.AsNoTracking().ToListAsync(cancellationToken);
        var allFuelTypes = await _dbContext.FuelTypes.AsNoTracking().ToListAsync(cancellationToken);
        var existingPackages = await _dbContext.FuelPackages.AsNoTracking().ToListAsync(cancellationToken);

        var suggestions = new List<object>();

        foreach (var voucher in uniqueCombos)
        {
            var cleanProvider = voucher.Provider.ToLowerInvariant().Trim();
            var station = allStations.FirstOrDefault(s => s.Name.ToLowerInvariant().Trim() == cleanProvider);
            if (station is null) continue;

            var fuelType = allFuelTypes.FirstOrDefault(ft =>
                ft.StationId == station.Id &&
                ft.Id == voucher.FuelTypeId);
            if (fuelType is null) continue;

            var exists = existingPackages.Any(pkg =>
                pkg.StationId == station.Id &&
                pkg.FuelTypeId == fuelType.Id &&
                pkg.Liters == (int)voucher.Liters);
            if (exists) continue;

            var generatedId = $"{station.Id}-{fuelType.Name.Replace(" ", "").ToLowerInvariant()}-{voucher.Liters}";
            suggestions.Add(new
            {
                suggestedId = generatedId,
                stationId = station.Id,
                stationName = station.Name,
                fuelTypeId = fuelType.Id,
                fuelName = fuelType.Name,
                liters = (int)voucher.Liters
            });
        }

        return Ok(suggestions);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FuelPackage request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.StationId) || string.IsNullOrWhiteSpace(request.FuelTypeId))
            return BadRequest("Id, StationId and FuelTypeId are required");

        var exists = await _dbContext.FuelPackages.AnyAsync(x => x.Id == request.Id, cancellationToken);
        if (exists)
            return Conflict($"Package with id '{request.Id}' already exists");

        request.CreatedAtUtc = DateTime.UtcNow;
        _dbContext.FuelPackages.Add(request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = request.Id }, request);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] FuelPackage request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelPackages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        entity.StationId = request.StationId;
        entity.FuelTypeId = request.FuelTypeId;
        entity.FuelName = request.FuelName;
        entity.Liters = request.Liters;
        entity.Price = request.Price;
        entity.OriginalPrice = request.OriginalPrice;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelPackages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.FuelPackages.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}
