using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.Stations;

[ApiController]
[Route("api/admin/stations")]
public class StationsController : ControllerBase
{
    private readonly IStationRepository _repository;

    public StationsController(IStationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var stations = await _repository.GetAllAsync();
        return Ok(stations);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var station = await _repository.GetByIdAsync(id);
        if (station == null)
            return NotFound(new { message = "Station not found" });
        return Ok(station);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStationRequest request)
    {
        var station = await _repository.CreateAsync(request);
        return Ok(station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateStationRequest request)
    {
        var station = await _repository.UpdateAsync(id, request);
        if (station == null)
            return NotFound(new { message = "Station not found" });
        return Ok(station);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted)
            return NotFound(new { message = "Station not found" });
        return Ok(new { message = "Station deleted" });
    }
}

[ApiController]
[Route("api/stations")]
public class PublicStationsController : ControllerBase
{
    private readonly IStationRepository _repository;

    public PublicStationsController(IStationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var stations = await _repository.GetAllAsync();
        return Ok(stations);
    }
}
