using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.CreateBrandStation;

public sealed class CreateBrandStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateBrandStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateBrandStationResult> HandleAsync(CreateBrandStationCommand command, CancellationToken ct = default)
    {
        var node = command.Node;

        if (node.BrandId == Guid.Empty ||
            string.IsNullOrWhiteSpace(node.Name))
            return new CreateBrandStationResult
            {
                Error = "BrandId and Name are required",
                Code = BrandStationRefusalCodes.BrandAndNameRequired
            };

        if (node.Lat is null || node.Lng is null ||
            !BrandStationCoordinates.IsValidLat(node.Lat.Value) ||
            !BrandStationCoordinates.IsValidLng(node.Lng.Value))
            return new CreateBrandStationResult
            {
                Error = "Valid Lat (-90..90) and Lng (-180..180) are required",
                Code = BrandStationRefusalCodes.CoordinatesInvalid
            };

        var BrandExists = await _context.Brands.AnyAsync(s => s.Id == node.BrandId, ct);
        if (!BrandExists)
            return new CreateBrandStationResult
            {
                Error = $"Brand '{node.BrandId}' does not exist",
                Code = BrandStationRefusalCodes.BrandMissing
            };

        // Derived from the coordinates when the caller did not name the АЗК, the same rule the bulk
        // import follows - so the form and the file cannot produce two different identities for one
        // physical station.
        var slug = await _context.Brands.Where(b => b.Id == node.BrandId)
            .Select(b => b.Slug).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(node.ExternalId))
        {
            node.ExternalId = string.IsNullOrWhiteSpace(slug)
                ? $"{node.BrandId}-{node.Lat:F5}-{node.Lng:F5}"
                : BrandStationCoordinates.DeriveId(slug, node.Lat!.Value, node.Lng!.Value);
        }

        var exists = await _context.BrandStations
            .AnyAsync(n => n.BrandId == node.BrandId && n.ExternalId == node.ExternalId, ct);
        if (exists)
            return new CreateBrandStationResult
            {
                Conflict = true,
                Error = $"Brand node with external id '{node.ExternalId}' already exists at this brand",
                Code = BrandStationRefusalCodes.ExternalIdConflict
            };

        node.Id = Guid.CreateVersion7();
        node.CreatedAtUtc = SystemClock.UtcNow;
        node.UpdatedAtUtc = node.CreatedAtUtc;
        _context.BrandStations.Add(node);
        await _context.SaveChangesAsync(ct);
        return new CreateBrandStationResult { Success = true, Node = node };
    }
}

public sealed class CreateBrandStationResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// The stable machine code of the refusal, when there was one — see
    /// <see cref="BrandStationRefusalCodes"/>. The admin renders its own sentence from this and
    /// shows <see cref="Error"/> only for a code it has not learnt.
    /// </summary>
    public string? Code { get; set; }

    public BrandStation? Node { get; set; }
}
