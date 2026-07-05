using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Purchases;

[ApiController]
[Route("api/admin/purchases")]
[Authorize(Roles = "Admin")]
public sealed class AdminPurchaseController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminPurchaseController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.Purchases
            .AsNoTracking()
            .Include(p => p.User)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        var item = await _dbContext.Purchases
            .AsNoTracking()
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpdatePurchaseRequest request, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Purchases.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Status))
            entity.Status = request.Status;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Purchases.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
            return NotFound();

        _dbContext.Purchases.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true });
    }
}

public sealed class UpdatePurchaseRequest
{
    public string? Status { get; set; }
}
