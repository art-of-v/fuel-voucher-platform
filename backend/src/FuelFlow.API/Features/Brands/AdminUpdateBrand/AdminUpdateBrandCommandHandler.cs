using System.Text.Json;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.AdminUpdateBrand;

/// <summary>
/// Renames and restyles a network, and records what changed in the audit trail.
/// </summary>
/// <remarks>
/// The summary the audit row carries is the point of the handler: "the network was renamed and the
/// logo changed" is what an operator reads on the audit screen three weeks later, and it can only
/// be written where the old values and the new ones are both in hand.
/// </remarks>
public sealed class AdminUpdateBrandCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminUpdateBrandCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<AdminUpdateBrandResult> HandleAsync(AdminUpdateBrandCommand command, CancellationToken ct = default)
    {
        var request = command.Brand;

        var Brand = await _context.Brands.FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (Brand is null) return new AdminUpdateBrandResult(false);

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

        var newValue = JsonSerializer.Serialize(new { Brand.Slug, Brand.Name, Brand.LogoText, Brand.Color, Brand.SortOrder });
        var summary = changes.Count > 0
            ? $"{Brand.Name}: {string.Join(", ", changes)}"
            : $"Updated brand {Brand.Name}";
        _eventService.RecordEvent(
            "Brand", command.Id.ToString(), "BrandUpdated",
            oldValue, newValue, command.UserId, command.UserName,
            summary,
            Brand.Slug);

        // One save for the brand and the audit row: the row and the change it describes commit
        // together, or neither does (planning #533).
        await _context.SaveChangesAsync(ct);

        return new AdminUpdateBrandResult(true);
    }
}
