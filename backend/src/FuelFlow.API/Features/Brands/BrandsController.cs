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

using FuelFlow.Features.Stations;
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
        var exists = await _context.Stations.AnyAsync(s => s.Id == request.Id, ct);
        if (exists)
            return Conflict(new { error = "Brand with this ID already exists" });

        var station = new Station
        {
            Id = request.Id,
            Name = request.Name,
            LogoText = request.LogoText,
            Color = request.Color,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _context.Stations.Add(station);
        await _context.SaveChangesAsync(ct);

        var userId = GetUserId();
        var newValue = JsonSerializer.Serialize(new { station.Id, station.Name });
        await _eventService.RecordEventAsync(
            "Brand", station.Id, "BrandCreated",
            null, newValue, userId, GetUserName(),
            $"Created brand {station.Name}",
            station.Id,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = station.Id }, station);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] BrandDto request, CancellationToken ct)
    {
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var oldName = station.Name;
        var oldLogoText = station.LogoText;
        var oldColor = station.Color;
        var oldSortOrder = station.SortOrder;
        var oldValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color, station.SortOrder });

        station.Name = request.Name;
        station.LogoText = request.LogoText;
        station.Color = request.Color;
        station.SortOrder = request.SortOrder;
        station.UpdatedAtUtc = DateTime.UtcNow;
        _context.Stations.Update(station);
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
        var newValue = JsonSerializer.Serialize(new { station.Name, station.LogoText, station.Color, station.SortOrder });
        var summary = changes.Count > 0
            ? $"{station.Name}: {string.Join(", ", changes)}"
            : $"Updated brand {station.Name}";
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
        var station = await _context.Stations.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (station is null) return NotFound();

        var name = station.Name;
        var fuelTypes = await _context.FuelTypes.Where(f => f.StationId == id).ToListAsync(ct);
        var fuelIds = fuelTypes.Select(f => f.Id).ToList();
        // Archive-the-offers view of the same question as the brand list above: this deletion path needs
        // the rows to mutate, not the read projection the card is built from.
        var offerIdsToArchive = await _context.FuelOffers
            .Where(o => fuelIds.Contains(o.FuelTypeId))
            .Select(o => o.Id).ToListAsync(ct);
        var packages = await _context.FuelOfferVolumes
            .Where(v => offerIdsToArchive.Contains(v.OfferId))
            .ToListAsync(ct);
        var offersToArchive = await _context.FuelOffers
            .Where(o => offerIdsToArchive.Contains(o.Id))
            .ToListAsync(ct);

        // Archive the whole branch instead of removing it. fuel_vouchers.fuel_type_id CASCADEs from
        // here, so the old RemoveRange cascade could take paid-for vouchers with it; #217 keeps
        // anything that affects history. The !IsDeleted filters keep all three out of the catalog.
        var archivedAt = DateTime.UtcNow;
        foreach (var package in packages)
        {
            package.IsDeleted = true;
            package.DeletedAtUtc = archivedAt;
        }
        // The offers go too. Archiving only their volumes would leave a live offer behind with no
        // sellable volume, and the next nominals edit would happily add volumes back to a brand that was
        // deleted - the offer is the row that says "this fuel is on sale here", so it is the row that has
        // to say no.
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
        station.IsDeleted = true;
        station.DeletedAtUtc = archivedAt;
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
