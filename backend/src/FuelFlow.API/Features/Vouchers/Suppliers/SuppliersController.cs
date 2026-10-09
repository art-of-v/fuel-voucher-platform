using System.Text.Json;
using System.Text.RegularExpressions;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// Suppliers — the external parties we buy vouchers from, as distinct from our own
/// <c>LegalEntity</c> companies. Staff only, because choosing a supplier on an import decides who the
/// stock is settled and exchanged with.
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

    /// <summary>
    /// The legal forms we accept. Kept as data rather than a free-text field so the admin UI can offer a
    /// fixed list and a saved supplier always round-trips to a form we know how to render.
    /// </summary>
    public static readonly string[] LegalForms = ["ФОП", "ТОВ", "ТзОВ", "ПАТ", "ФОП-ЄЗ", "Інше"];

    private static readonly Regex EdrIpnPattern = new(@"^\d{8,10}$", RegexOptions.Compiled);
    private static readonly Regex RnkrrPattern = new(@"^\d{5}$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> List(
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var query = _context.Suppliers.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(s => s.IsActive);

        var rows = await query
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.LegalForm, s.Phone, s.Email, s.EdrIpn, s.Rnkrr, s.Address, s.Notes, s.IsActive))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>The accepted legal forms, so the UI never invents one we cannot validate.</summary>
    [HttpGet("legal-forms")]
    public ActionResult<IReadOnlyList<string>> LegalFormOptions() => Ok(LegalForms);

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(CreateSupplierRequest request, CancellationToken ct = default)
    {
        if (Validate(request) is { } error)
            return BadRequest(new { error });

        var now = DateTime.UtcNow;
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            LegalForm = request.LegalForm?.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            EdrIpn = request.EdrIpn?.Trim(),
            Rnkrr = request.Rnkrr?.Trim(),
            Address = request.Address?.Trim(),
            Notes = request.Notes?.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierCreated", supplier, ct);
        return Ok(ToDto(supplier));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        if (Validate(request) is { } error)
            return BadRequest(new { error });

        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        supplier.Name = request.Name!.Trim();
        supplier.LegalForm = request.LegalForm?.Trim();
        supplier.Phone = request.Phone?.Trim();
        supplier.Email = request.Email?.Trim();
        supplier.EdrIpn = request.EdrIpn?.Trim();
        supplier.Rnkrr = request.Rnkrr?.Trim();
        supplier.Address = request.Address?.Trim();
        supplier.Notes = request.Notes?.Trim();
        if (request.IsActive.HasValue) supplier.IsActive = request.IsActive.Value;
        supplier.UpdatedAtUtc = DateTime.UtcNow;

        // The context's default tracking behaviour is NoTracking, so an entity loaded here is not
        // tracked: without Update(...) these property changes never reach SaveChanges, while the
        // outbox row added in RecordAsync does - which made the response look correct and the write
        // silently lost. ProvidersController.Update carries the same call for the same reason.
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierUpdated", supplier, ct);
        return Ok(ToDto(supplier));
    }

    /// <summary>
    /// Deactivate rather than delete. Past vouchers and exchange rows point at this supplier, and that
    /// audit trail has to keep naming a real counterparty — a hard delete would either be blocked by the
    /// FK or silently orphan the history.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        if (!supplier.IsActive) return NoContent();

        supplier.IsActive = false;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        // Update(...) because the context loads with NoTracking - see the note in Update.
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierDeactivated", supplier, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<SupplierDto>> Reactivate(Guid id, CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        supplier.IsActive = true;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        // Update(...) because the context loads with NoTracking - see the note in Update.
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync(ct);

        await RecordAsync("SupplierReactivated", supplier, ct);
        return Ok(ToDto(supplier));
    }

    private static SupplierDto ToDto(Supplier s) => new(
        s.Id, s.Name, s.LegalForm, s.Phone, s.Email, s.EdrIpn, s.Rnkrr, s.Address, s.Notes, s.IsActive);

    /// <summary>
    /// Only the name is mandatory. Everything else depends on the legal form — an ФОП has an ІПН and an
    /// LLC has an ЄДРОПУ — so rejecting a blank there would reject half the suppliers we actually deal with.
    /// </summary>
    private static string? Validate(CreateSupplierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Supplier name is required.";

        if (request.LegalForm is { Length: > 0 } form && !LegalForms.Contains(form.Trim()))
            return $"Unknown legal form '{form}'. Expected one of: {string.Join(", ", LegalForms)}.";

        if (!string.IsNullOrWhiteSpace(request.EdrIpn) && !EdrIpnPattern.IsMatch(request.EdrIpn.Trim()))
            return "ЄДРОПУ/ІПН must be 8-10 digits.";

        if (!string.IsNullOrWhiteSpace(request.Rnkrr) && !RnkrrPattern.IsMatch(request.Rnkrr.Trim()))
            return "РНОКР must be 5 digits.";

        if (!string.IsNullOrWhiteSpace(request.Email) && !request.Email.Contains('@'))
            return "Email is not valid.";

        return null;
    }

    private Task RecordAsync(string type, Supplier supplier, CancellationToken ct)
    {
        var actorId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : (Guid?)null;
        if (actorId is null) return Task.CompletedTask;

        var actorName = User.FindFirst("first_name")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        // old_value/new_value are jsonb, not text: an unserialised string here is a 22P02 at SaveChanges,
        // and it fails in production only - the in-memory provider does not parse jsonb.
        var snapshot = JsonSerializer.Serialize(new
        {
            supplier.Id,
            supplier.Name,
            supplier.LegalForm,
            supplier.Phone,
            supplier.Email,
            supplier.EdrIpn,
            supplier.Rnkrr,
            supplier.Address,
            supplier.Notes,
            supplier.IsActive
        });

        return _events.RecordEventAsync("Supplier", supplier.Id.ToString(), type, null, snapshot,
            actorId.Value, actorName, $"{type}: {supplier.Name}", "all", ct);
    }
}

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? LegalForm,
    string? Phone,
    string? Email,
    string? EdrIpn,
    string? Rnkrr,
    string? Address,
    string? Notes,
    bool IsActive);

public class CreateSupplierRequest
{
    public string? Name { get; set; }
    public string? LegalForm { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? EdrIpn { get; set; }
    public string? Rnkrr { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpdateSupplierRequest : CreateSupplierRequest
{
    public bool? IsActive { get; set; }
}