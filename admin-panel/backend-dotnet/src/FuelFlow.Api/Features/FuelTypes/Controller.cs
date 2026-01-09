using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.FuelTypes;

[ApiController]
[Route("api/admin/fuel-types")]
public class FuelTypesController : ControllerBase
{
    private readonly IFuelTypeRepository _repository;

    public FuelTypesController(IFuelTypeRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var fuelTypes = await _repository.GetAllAsync();
        return Ok(fuelTypes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var fuelType = await _repository.GetByIdAsync(id);
        if (fuelType == null)
            return NotFound(new { message = "Fuel type not found" });
        return Ok(fuelType);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFuelTypeRequest request)
    {
        var fuelType = await _repository.CreateAsync(request);
        return Ok(fuelType);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateFuelTypeRequest request)
    {
        var fuelType = await _repository.UpdateAsync(id, request);
        if (fuelType == null)
            return NotFound(new { message = "Fuel type not found" });
        return Ok(fuelType);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted)
            return NotFound(new { message = "Fuel type not found" });
        return Ok(new { message = "Fuel type deleted" });
    }
}
