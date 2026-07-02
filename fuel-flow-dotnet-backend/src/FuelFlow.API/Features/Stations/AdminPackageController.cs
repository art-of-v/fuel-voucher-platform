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
