using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers;

[ApiController]
[Route("api/admin/vouchers")]
[Authorize(Roles = "Admin")]
public sealed class AdminVoucherController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminVoucherController(ApplicationDbContext dbContext)
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
        var query = _dbContext.Vouchers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v =>
                v.Provider.Contains(search) ||
                v.FuelType.Contains(search) ||
                v.ExternalId.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(v => v.Status == status);
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
        var item = await _dbContext.Vouchers
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVoucherRequest request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Status))
            entity.Status = request.Status;
        if (request.AssignedToUserId.HasValue)
            entity.AssignedToUserId = request.AssignedToUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.Vouchers.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}

public sealed class UpdateVoucherRequest
{
    public string? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
