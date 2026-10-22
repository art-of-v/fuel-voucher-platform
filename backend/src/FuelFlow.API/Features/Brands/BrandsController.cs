using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Features.Brands.GetFuelPriceHistory;
using FuelFlow.Features.Brands.GetFuelPriceAt;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Features.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/brands")]
public sealed class BrandsController : ControllerBase
{
private readonly GetBrandsQueryHandler _getAll;
    private readonly GetBrandByIdQueryHandler _getById;
    private readonly GetBrandHistoryQueryHandler _getHistory;
    private readonly GetFuelPriceHistoryQueryHandler _getPriceHistory;
    private readonly GetFuelPriceAtQueryHandler _getPriceAt;
    private readonly AuditEventService _eventService;
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;

    // Every price this controller writes goes through here, so the history cannot be missed by the one
    // endpoint that forgets. See PriceHistoryRecorder for why that is a service rather than a comment.
    private readonly PriceHistoryRecorder _priceRecorder;
    private readonly BrandSlugResolver _brands;

    public BrandsController(
        GetBrandsQueryHandler getAll,
        GetBrandByIdQueryHandler getById,
        GetBrandHistoryQueryHandler getHistory,
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AuditEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        PriceHistoryRecorder priceRecorder,
        BrandSlugResolver brands)
    {
        _getAll = getAll;
        _getById = getById;
        _getHistory = getHistory;
        _getPriceHistory = getPriceHistory;
        _getPriceAt = getPriceAt;
        _eventService = eventService;
        _context = context;
        _notifications = notifications;
        _priceRecorder = priceRecorder;
        _brands = brands;
    }

    private static readonly List<int> DefaultNominals = new() { 2, 3, 5, 10, 20, 50 };

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetBrandsQuery(), ct));

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetBrandByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// The audit trail for one brand.
    /// </summary>
    /// <remarks>
    /// Scoped by <b>slug</b>, not id, and that is what keeps the history intact across the uuid
    /// migration: <c>audit_event_outbox.scope_id</c> has always held the network's own code, so the
    /// rows written before the id changed still resolve. Only <c>aggregate_id</c> held the old text
    /// id, and that is a label on the audit screen rather than the lookup - which is why nothing had
    /// to be rewritten to keep this endpoint working.
    /// </remarks>
    [HttpGet("{id}/history")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetHistory(
        [FromRoute] Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var slug = await _brands.SlugOfAsync(id, ct);
        if (slug is null) return NotFound();

        return Ok(await _getHistory.HandleAsync(new GetBrandHistoryQuery(slug, limit), ct));
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Create([FromBody] BrandDto request, CancellationToken ct)
    {
        var slug = (request.Slug ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { error = "Slug is required" });

        var exists = await _context.Brands.AnyAsync(s => s.Slug == slug, ct);
        if (exists)
            return Conflict(new { error = "Brand with this slug already exists" });

        // The id is the server's to assign. A caller that sent one is ignored rather than rejected:
        // the admin form posts back the row it was editing, and refusing that would break the one
        // screen that creates brands.
        var Brand = new Brand
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Name = request.Name,
            LogoText = request.LogoText,
            Color = request.Color,
            SortOrder = request.SortOrder,
            CreatedAtUtc = SystemClock.UtcNow,
            UpdatedAtUtc = SystemClock.UtcNow
        };
        _context.Brands.Add(Brand);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { Brand.Slug, Brand.Name });
        _eventService.RecordEvent(
            "Brand", Brand.Id.ToString(), "BrandCreated",
            null, newValue, userId, GetUserName(),
            $"Created brand {Brand.Name}",
            Brand.Slug);

        // One save for the brand and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = Brand.Id }, Brand);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] BrandDto request, CancellationToken ct)
    {
        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

        var oldName = Brand.Name;
        var oldLogoText = Brand.LogoText;
        var oldColor = Brand.Color;
        var oldSortOrder = Brand.SortOrder;
        var oldValue = JsonSerializer.Serialize(new { Brand.Slug, Brand.Name, Brand.LogoText, Brand.Color, Brand.SortOrder });

        // The slug is immutable over HTTP. It is the importer's crosswalk key and every external
        // reference to this network resolves through it, so renaming it here would silently re-point
        // an import file at nothing. Changing a slug is a data operation with a crosswalk, not a form
        // field - and the uniqueness index is the only thing that would have caught the damage.
        Brand.Name = request.Name;
        Brand.LogoText = request.LogoText;
        Brand.Color = request.Color;
        Brand.SortOrder = request.SortOrder;
        Brand.UpdatedAtUtc = SystemClock.UtcNow;
        _context.Brands.Update(Brand);

        var changes = new List<string>();
        if (oldName != request.Name)
            changes.Add($"name {oldName}  {request.Name}");
        if (oldLogoText != request.LogoText)
            changes.Add($"logo {oldLogoText}  {request.LogoText}");
        if (oldColor != request.Color)
            changes.Add($"color {oldColor}  {request.Color}");
        if (oldSortOrder != request.SortOrder)
            changes.Add($"sort order {oldSortOrder}  {request.SortOrder}");

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { Brand.Slug, Brand.Name, Brand.LogoText, Brand.Color, Brand.SortOrder });
        var summary = changes.Count > 0
            ? $"{Brand.Name}: {string.Join(", ", changes)}"
            : $"Updated brand {Brand.Name}";
        _eventService.RecordEvent(
            "Brand", id.ToString(), "BrandUpdated",
            oldValue, newValue, userId, GetUserName(),
            summary,
            Brand.Slug);

        // One save for the brand and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthPolicies.CatalogueWrite)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

        var name = Brand.Name;
        var slug = Brand.Slug;
        var fuelTypes = await _context.FuelTypes.Where(f => f.BrandId == id).ToListAsync(ct);

        // Archive the whole branch instead of removing it. fuel_vouchers.fuel_type_id CASCADEs from
        // here, so the old RemoveRange cascade could take paid-for vouchers with it; #217 keeps
        // anything that affects history. The !IsDeleted filters keep both out of the catalog.
        var archivedAt = SystemClock.UtcNow;

        foreach (var fuelType in fuelTypes)
        {
            // Archiving the fuel IS saying "this fuel is not on sale here". It used to be a second row
            // doing that job: a live offer kept the fuel on the shelf even with every fuel row behind
            // it flagged deleted. One row that says no is one row that cannot be forgotten.
            fuelType.IsDeleted = true;
            fuelType.DeletedAtUtc = archivedAt;
        }
        Brand.IsDeleted = true;
        Brand.DeletedAtUtc = archivedAt;

        var userId = GetUserId();
        _eventService.RecordEvent(
            "Brand", id.ToString(), "BrandDeleted",
            JsonSerializer.Serialize(new { name, slug }), "{}", userId, GetUserName(),
            $"Deleted brand {name}",
            slug);

        // One save for the archived brand, its fuels and the audit row: the row and the change it
        // describes commit together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return Ok(new { success = true });
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (first is not null && last is not null) return $"{first} ${last}";
        if (first is not null) return first;
        if (last is not null) return last;
        return User.FindFirst(ClaimTypes.Name)?.Value;
    }

}
