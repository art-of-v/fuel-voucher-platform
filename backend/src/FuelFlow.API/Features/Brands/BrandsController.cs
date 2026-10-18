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
using FuelFlow.Features.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Brands;

[ApiController]
[Route("api/admin/brands")]
[Authorize(Policy = "Staff")]
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
    // endpoint that forgets. See OfferPriceRecorder for why that is a service rather than a comment.
    private readonly OfferPriceRecorder _priceRecorder;

    public BrandsController(
        GetBrandsQueryHandler getAll,
        GetBrandByIdQueryHandler getById,
        GetBrandHistoryQueryHandler getHistory,
        GetFuelPriceHistoryQueryHandler getPriceHistory,
        GetFuelPriceAtQueryHandler getPriceAt,
        AuditEventService eventService,
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        OfferPriceRecorder priceRecorder)
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
    }

    private static readonly List<int> DefaultNominals = new() { 2, 3, 5, 10, 20, 50 };

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _getAll.HandleAsync(new GetBrandsQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var result = await _getById.HandleAsync(new GetBrandByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(
        [FromRoute] string id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await _getHistory.HandleAsync(new GetBrandHistoryQuery(id, limit), ct));
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BrandDto request, CancellationToken ct)
    {
        var exists = await _context.Brands.AnyAsync(s => s.Id == request.Id, ct);
        if (exists)
            return Conflict(new { error = "Brand with this ID already exists" });

        var Brand = new Brand
        {
            Id = request.Id,
            Name = request.Name,
            LogoText = request.LogoText,
            Color = request.Color,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.Brands.Add(Brand);
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { Brand.Id, Brand.Name });
        await _eventService.RecordEventAsync(
            "Brand", Brand.Id, "BrandCreated",
            null, newValue, userId, GetUserName(),
            $"Created brand {Brand.Name}",
            Brand.Id,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = Brand.Id }, Brand);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] BrandDto request, CancellationToken ct)
    {
        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

        var oldName = Brand.Name;
        var oldLogoText = Brand.LogoText;
        var oldColor = Brand.Color;
        var oldSortOrder = Brand.SortOrder;
        var oldValue = JsonSerializer.Serialize(new { Brand.Name, Brand.LogoText, Brand.Color, Brand.SortOrder });

        Brand.Name = request.Name;
        Brand.LogoText = request.LogoText;
        Brand.Color = request.Color;
        Brand.SortOrder = request.SortOrder;
        Brand.UpdatedAtUtc = DateTime.UtcNow;
        _context.Brands.Update(Brand);
        await _context.SaveChangesAsync(ct);

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
        var newValue = JsonSerializer.Serialize(new { Brand.Name, Brand.LogoText, Brand.Color, Brand.SortOrder });
        var summary = changes.Count > 0
            ? $"{Brand.Name}: {string.Join(", ", changes)}"
            : $"Updated brand {Brand.Name}";
        await _eventService.RecordEventAsync(
            "Brand", id, "BrandUpdated",
            oldValue, newValue, userId, GetUserName(),
            summary,
            id,
            ct);

        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id, CancellationToken ct)
    {
        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (Brand is null) return NotFound();

        var name = Brand.Name;
        var fuelTypes = await _context.FuelTypes.Where(f => f.BrandId == id).ToListAsync(ct);
        var fuelIds = fuelTypes.Select(f => f.Id).ToList();
        // Archive-the-offers view of the same question as the brand list above: this deletion path needs
        // the rows to mutate, not the read projection the card is built from.
        var offerIdsToArchive = await _context.FuelOffers
            .Where(o => fuelIds.Contains(o.FuelTypeId))
            .Select(o => o.Id).ToListAsync(ct);
        var offersToArchive = await _context.FuelOffers
            .Where(o => offerIdsToArchive.Contains(o.Id))
            .ToListAsync(ct);

        // Archive the whole branch instead of removing it. fuel_vouchers.fuel_type_id CASCADEs from
        // here, so the old RemoveRange cascade could take paid-for vouchers with it; #217 keeps
        // anything that affects history. The !IsDeleted filters keep all three out of the catalog.
        var archivedAt = DateTime.UtcNow;
        // The sizes are not touched, because they belong to the fuel and the fuel is archived below -
        // so they are already out of the catalogue. Archiving the offer is what says "this fuel is
        // not on sale here", and it is the row that has to say no: a live offer would keep the fuel
        // on the shelf even with every fuel row behind it flagged deleted.
        foreach (var offer in offersToArchive)
        {
            offer.IsDeleted = true;
            offer.DeletedAtUtc = archivedAt;
        }

        foreach (var fuelType in fuelTypes)
        {
            fuelType.IsDeleted = true;
            fuelType.DeletedAtUtc = archivedAt;
        }
        Brand.IsDeleted = true;
        Brand.DeletedAtUtc = archivedAt;
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        await _eventService.RecordEventAsync(
            "Brand", id, "BrandDeleted",
            JsonSerializer.Serialize(new { name }), "{}", userId, GetUserName(),
            $"Deleted brand {name}",
            id,
            ct);

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
