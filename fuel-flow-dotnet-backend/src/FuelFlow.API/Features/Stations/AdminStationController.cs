using FuelFlow.Features.Stations.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/admin/stations")]
[Authorize(Roles = "Admin")]
public sealed class AdminStationController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminStationController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.Stations
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.Stations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Station request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.LogoText))
            return BadRequest("Id, Name and LogoText are required");

        var exists = await _dbContext.Stations.AnyAsync(x => x.Id == request.Id, cancellationToken);
        if (exists)
            return Conflict($"Station with id '{request.Id}' already exists");

        request.CreatedAtUtc = DateTime.UtcNow;
        _dbContext.Stations.Add(request);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] Station request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Stations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        entity.Name = request.Name;
        entity.Color = request.Color;
        entity.LogoText = request.LogoText;
        entity.Address = request.Address;
        entity.Phone = request.Phone;
        entity.StationType = request.StationType;
        entity.Lat = request.Lat;
        entity.Lng = request.Lng;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Stations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.Stations.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}
