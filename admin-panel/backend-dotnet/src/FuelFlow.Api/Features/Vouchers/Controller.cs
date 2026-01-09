using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.Vouchers;

[ApiController]
[Route("api/vouchers")]
public class VouchersController : ControllerBase
{
    private readonly IVoucherRepository _repository;

    public VouchersController(IVoucherRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? provider, 
        [FromQuery] string? fuelType, [FromQuery] int page = 1, [FromQuery] int limit = 50)
    {
        var vouchers = await _repository.GetAllAsync();
        
        if (!string.IsNullOrEmpty(status))
            vouchers = vouchers.Where(v => v.Status == status);
        if (!string.IsNullOrEmpty(provider))
            vouchers = vouchers.Where(v => v.Provider == provider);
        if (!string.IsNullOrEmpty(fuelType))
            vouchers = vouchers.Where(v => v.FuelType == fuelType);

        var paged = vouchers.Skip((page - 1) * limit).Take(limit);
        return Ok(new { data = paged });
    }

    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable()
    {
        var vouchers = await _repository.GetAvailableVouchersAsync();
        return Ok(vouchers);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyVouchers()
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";
        var vouchers = await _repository.GetUserVouchersAsync(userId);
        return Ok(vouchers);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var voucher = await _repository.GetByIdAsync(id);
        if (voucher == null)
            return NotFound(new { message = "Voucher not found" });
        return Ok(voucher);
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateVoucherRequest request)
    {
        var voucher = await _repository.UpdateAsync(id, request);
        if (voucher == null)
            return NotFound(new { message = "Voucher not found" });
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted)
            return NotFound(new { message = "Voucher not found" });
        return Ok(new { message = "Voucher deleted" });
    }

    [HttpPost("bulk-action")]
    public async Task<IActionResult> BulkAction([FromBody] BulkActionRequest request)
    {
        if (request.Action == "delete_all")
        {
            var count = await _repository.DeleteAllAsync();
            return Ok(new { success = true });
        }

        if (request.Ids == null || request.Ids.Count == 0)
            return BadRequest(new { error = "No IDs provided" });

        var updates = new UpdateVoucherRequest();
        
        if (request.Action == "activate")
            updates = updates with { Status = "available" };
        else if (request.Action == "expire")
            updates = updates with { Status = "expired" };
        else if (request.Action == "assign")
        {
            if (string.IsNullOrEmpty(request.TargetUserId))
                return BadRequest(new { error = "Target User ID required" });
            updates = updates with { Status = "assigned", AssignedToUserId = request.TargetUserId };
        }
        else if (request.Action == "delete")
        {
            foreach (var id in request.Ids)
                await _repository.DeleteAsync(id);
            return Ok(new { success = true, count = request.Ids.Count });
        }

        foreach (var id in request.Ids)
            await _repository.UpdateAsync(id.ToString(), updates);

        return Ok(new { success = true, count = request.Ids.Count });
    }
}

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private readonly IVoucherRepository _repository;

    public InventoryController(IVoucherRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetInventory()
    {
        try
        {
            var inventory = await _repository.GetInventoryAggregationAsync();
            return Ok(inventory);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to fetch inventory", details = ex.Message });
        }
    }
}

public record BulkActionRequest
{
    public string Action { get; init; } = string.Empty;
    public List<Guid> Ids { get; init; } = new();
    public string? TargetUserId { get; init; }
}
