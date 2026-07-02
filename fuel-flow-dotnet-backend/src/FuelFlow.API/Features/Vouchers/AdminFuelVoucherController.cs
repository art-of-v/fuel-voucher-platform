using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/fuel-vouchers")]
[Authorize(Roles = "Admin")]
public sealed class AdminFuelVoucherController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminFuelVoucherController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _dbContext.FuelVouchers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v =>
                v.Provider.Contains(search) ||
                v.FuelTypeId.Contains(search) ||
                v.VoucherNumber.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VoucherStatus>(status, out var parsedStatus))
        {
            query = query.Where(v => v.Status == parsedStatus);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .AsNoTracking()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderByDescending(v => v.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(new { items, total });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.FuelVouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateFuelVoucherRequest request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelVouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<VoucherStatus>(request.Status, out var parsedStatus))
            entity.Status = parsedStatus;
        if (request.AssignedToUserId.HasValue)
            entity.AssignedToUserId = request.AssignedToUserId.Value.ToString();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FuelVouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.FuelVouchers.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}

public sealed class UpdateFuelVoucherRequest
{
    public string? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
