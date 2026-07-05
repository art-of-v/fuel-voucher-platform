using FuelFlow.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/packages")]
public sealed class PackageController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public PackageController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
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
}
