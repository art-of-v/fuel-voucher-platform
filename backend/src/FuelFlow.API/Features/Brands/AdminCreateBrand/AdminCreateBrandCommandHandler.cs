using System.Text.Json;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminCreateBrand;

/// <summary>
/// Puts a new network into the catalogue, and leaves behind the audit row that says who added it.
/// </summary>
/// <remarks>
/// The rules live here rather than in the controller, because a rule that only exists inside an
/// action method cannot be tested without an HTTP pipeline around it - and these are the rules an
/// operator hits first: a brand with no slug, and a brand whose slug another one already owns.
/// </remarks>
public sealed class AdminCreateBrandCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminCreateBrandCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<AdminCreateBrandResult> HandleAsync(AdminCreateBrandCommand command, CancellationToken ct = default)
    {
        var request = command.Brand;

        var slug = (request.Slug ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(slug))
            return new AdminCreateBrandResult
            {
                Error = "Slug is required",
                Code = BrandRefusalCodes.SlugRequired
            };

        var exists = await _context.Brands.AnyAsync(s => s.Slug == slug, ct);
        if (exists)
            return new AdminCreateBrandResult
            {
                Error = "Brand with this slug already exists",
                Conflict = true,
                Code = BrandRefusalCodes.SlugTaken
            };

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

        var newValue = JsonSerializer.Serialize(new { Brand.Slug, Brand.Name });
        _eventService.RecordEvent(
            "Brand", Brand.Id.ToString(), "BrandCreated",
            null, newValue, command.UserId, command.UserName,
            $"Created brand {Brand.Name}",
            Brand.Slug);

        // One save for the brand and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminCreateBrandResult { Success = true, Brand = Brand };
    }
}
