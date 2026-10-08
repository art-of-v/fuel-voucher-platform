using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// Suppliers — the companies vouchers are bought from, as distinct from the brand printed on them. Staff
/// only, because choosing a supplier on an import decides who the stock will be settled and exchanged with.
/// </summary>
[ApiController]
[Route("api/admin/suppliers")]
[Authorize(Policy = "Staff")]
public sealed class SuppliersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _events;

    public SuppliersController(ApplicationDbContext context, ProviderEventService events)
    {
        _context = context;
        _events = events;
    }

    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _context.Suppliers.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        var rows = await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(s.Id, s.Name, s.ContactInfo, s.StationId, s.IsActive))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { error = "Supplier name is required." });

        if (await _context.Suppliers.AnyAsync(s => s.Name == name, ct))
            return Conflict(new { error = $"A supplier named '{name}' already exists." });

        var now = DateTime.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = name,
            ContactInfo = request.ContactInfo?.Trim(),
            StationId = request.StationId?.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierCreated", supplier.Id.ToString(), name, "all", ct);

        return Ok(new SupplierDto(supplier.Id, supplier.Name, supplier.ContactInfo, supplier.StationId, supplier.IsActive));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new { error = "Supplier name is required." });

        if (await _context.Suppliers.AnyAsync(s => s.Name == name && s.Id != id, ct))
            return Conflict(new { error = $"A supplier named '{name}' already exists." });

        supplier.Name = name;
        supplier.ContactInfo = request.ContactInfo?.Trim();
        supplier.StationId = request.StationId?.Trim();
        if (request.IsActive.HasValue) supplier.IsActive = request.IsActive.Value;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierUpdated", supplier.Id.ToString(), name, "all", ct);

        return Ok(new SupplierDto(supplier.Id, supplier.Name, supplier.ContactInfo, supplier.StationId, supplier.IsActive));
    }

    /// <summary>
    /// Deactivate rather than delete: past vouchers keep pointing at this supplier, and Restrict would
    /// refuse the delete anyway. The row has to survive so old exchanges still name a real counterparty.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        supplier.IsActive = false;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierDeactivated", supplier.Id.ToString(), supplier.Name, "all", ct);

        return NoContent();
    }

    private Task RecordAsync(string type, string entityId, string name, string subject, CancellationToken ct)
    {
        var actorId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : (Guid?)null;
        if (actorId is null) return Task.CompletedTask;

        var actorName = User.FindFirst("first_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return _events.RecordEventAsync("Supplier", entityId, type, null, name, actorId.Value, actorName, $"{type}: {name}", subject, ct);
    }
}

public sealed record SupplierDto(Guid Id, string Name, string? ContactInfo, string? StationId, bool IsActive);

public sealed class CreateSupplierRequest
{
    public string? Name { get; set; }
    public string? ContactInfo { get; set; }
    public string? StationId { get; set; }
}

public sealed class UpdateSupplierRequest
{
    public string? Name { get; set; }
    public string? ContactInfo { get; set; }
    public string? StationId { get; set; }
    public bool? IsActive { get; set; }
}