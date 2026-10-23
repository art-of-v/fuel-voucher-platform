using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.UpdateBrandStation;

public sealed class UpdateBrandStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateBrandStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<UpdateBrandStationResult> HandleAsync(UpdateBrandStationCommand command, CancellationToken ct = default)
    {
        var updated = command.Updated;

        if (updated.BrandId == Guid.Empty || string.IsNullOrWhiteSpace(updated.Name))
            return new UpdateBrandStationResult { Error = "BrandId and Name are required" };

        if (updated.Lat is null || updated.Lng is null ||
            !BrandStationCoordinates.IsValidLat(updated.Lat.Value) ||
            !BrandStationCoordinates.IsValidLng(updated.Lng.Value))
            return new UpdateBrandStationResult { Error = "Valid Lat (-90..90) and Lng (-180..180) are required" };

        var entity = await _context.BrandStations.FirstOrDefaultAsync(n => n.Id == command.Id, ct);
        if (entity is null) return new UpdateBrandStationResult { NotFound = true };

        var BrandExists = await _context.Brands.AnyAsync(s => s.Id == updated.BrandId, ct);
        if (!BrandExists)
            return new UpdateBrandStationResult { Error = $"Brand '{updated.BrandId}' does not exist" };

        entity.BrandId = updated.BrandId;
        entity.Name = updated.Name;
        entity.Address = updated.Address;
        entity.Phone = updated.Phone;
        entity.City = updated.City;
        entity.Lat = updated.Lat;
        entity.Lng = updated.Lng;
        entity.UpdatedAtUtc = SystemClock.UtcNow;
        _context.BrandStations.Update(entity);

        await _context.SaveChangesAsync(ct);
        return new UpdateBrandStationResult { Success = true };
    }
}

public sealed class UpdateBrandStationResult
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public string? Error { get; set; }
}
