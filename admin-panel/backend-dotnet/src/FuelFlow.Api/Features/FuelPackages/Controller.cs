using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.FuelPackages;

[ApiController]
[Route("api/admin/packages")]
public class FuelPackagesController : ControllerBase
{
    private readonly IFuelPackageRepository _repository;

    public FuelPackagesController(IFuelPackageRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var packages = await _repository.GetAllAsync();
        return Ok(packages);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var package = await _repository.GetByIdAsync(id);
        if (package == null)
            return NotFound(new { message = "Package not found" });
        return Ok(package);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFuelPackageRequest request)
    {
        var package = await _repository.CreateAsync(request);
        return Ok(package);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateFuelPackageRequest request)
    {
        var package = await _repository.UpdateAsync(id, request);
        if (package == null)
            return NotFound(new { message = "Package not found" });
        return Ok(package);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted)
            return NotFound(new { message = "Package not found" });
        return Ok(new { success = true });
    }
}

[ApiController]
[Route("api/packages")]
public class PublicPackagesController : ControllerBase
{
    private readonly IFuelPackageRepository _repository;

    public PublicPackagesController(IFuelPackageRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var packages = await _repository.GetAllAsync();
        return Ok(packages);
    }
}
