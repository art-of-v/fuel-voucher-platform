using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/fuel-types")]
public sealed class AdminFuelTypeController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminFuelTypeController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.FuelTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.FuelTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] FuelTypeEntity request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.StationId) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Id, StationId and Name are required");

        var exists = await _dbContext.FuelTypes.AnyAsync(x => x.Id == request.Id, cancellationToken);
        if (exists)
            return Conflict($"FuelType with id '{request.Id}' already exists");

        request.CreatedAtUtc = DateTime.UtcNow;
        _dbContext.FuelTypes.Add(request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] FuelTypeEntity request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        entity.Name = request.Name;
        entity.StationId = request.StationId;
        entity.BasePrice = request.BasePrice;
        entity.DiscountPrice = request.DiscountPrice;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.FuelTypes.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}
