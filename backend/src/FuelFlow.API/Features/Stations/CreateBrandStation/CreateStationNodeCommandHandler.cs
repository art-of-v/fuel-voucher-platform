using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.CreateBrandStation;

public sealed class CreateBrandStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateBrandStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateBrandStationResult> HandleAsync(CreateBrandStationCommand command, CancellationToken ct = default)
    {
        var node = command.Node;

        if (string.IsNullOrWhiteSpace(node.Id) ||
            string.IsNullOrWhiteSpace(node.BrandId) ||
            string.IsNullOrWhiteSpace(node.Name))
            return new CreateBrandStationResult { Error = "Id, BrandId and Name are required" };

        if (node.Lat is null || node.Lng is null ||
            !BrandStationCoordinates.IsValidLat(node.Lat.Value) ||
            !BrandStationCoordinates.IsValidLng(node.Lng.Value))
            return new CreateBrandStationResult { Error = "Valid Lat (-90..90) and Lng (-180..180) are required" };

        var BrandExists = await _context.Brands.AnyAsync(s => s.Id == node.BrandId, ct);
        if (!BrandExists)
            return new CreateBrandStationResult { Error = $"Brand (brand) '{node.BrandId}' does not exist" };

        var exists = await _context.BrandStations.AnyAsync(n => n.Id == node.Id, ct);
        if (exists)
            return new CreateBrandStationResult { Conflict = true, Error = $"Brand node with id '{node.Id}' already exists" };

        node.CreatedAtUtc = DateTime.UtcNow;
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
    public BrandStation? Node { get; set; }
}
