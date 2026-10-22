using System.Text.Json;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminDeleteBrand;

/// <summary>
/// Archives a network together with the fuels it sells.
/// </summary>
/// <remarks>
/// The whole branch goes, not just the network row: a live fuel under an archived network would
/// still be listed by the storefront queries that read the fuels directly.
/// </remarks>
public sealed class AdminDeleteBrandCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminDeleteBrandCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<AdminDeleteBrandResult> HandleAsync(AdminDeleteBrandCommand command, CancellationToken ct = default)
    {
        var Brand = await _context.Brands
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (Brand is null) return new AdminDeleteBrandResult(false);

        var name = Brand.Name;
        var slug = Brand.Slug;
        var fuelTypes = await _context.FuelTypes
            .AsTracking()
            .Where(f => f.BrandId == command.Id)
            .ToListAsync(ct);

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
        await _context.SaveChangesAsync(ct);

        await _eventService.RecordEventAsync(
            "Brand", command.Id.ToString(), "BrandDeleted",
            JsonSerializer.Serialize(new { name, slug }), "{}", command.UserId, command.UserName,
            $"Deleted brand {name}",
            slug,
            ct);

        return new AdminDeleteBrandResult(true);
    }
}
