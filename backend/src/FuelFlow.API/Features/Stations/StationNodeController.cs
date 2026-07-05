using FuelFlow.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations;

[ApiController]
[Route("api/station-nodes")]
public sealed class StationNodeController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public StationNodeController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.StationNodes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("station/{stationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByStation([FromRoute] string stationId, CancellationToken cancellationToken)
    {
        var items = await _dbContext.StationNodes
            .AsNoTracking()
            .Where(x => x.StationId == stationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}
