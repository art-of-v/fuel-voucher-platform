using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.FuelPrices.GetFuelPrices;

public sealed class GetFuelPricesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetFuelPricesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPriceDto>> HandleAsync(GetFuelPricesQuery query, CancellationToken ct = default)
    {
        var packages = await _context.FuelPackages
            .AsNoTracking()
            .OrderBy(p => p.StationId)
            .ThenBy(p => p.FuelName)
            .ThenBy(p => p.Liters)
            .Select(p => new FuelPriceDto
            {
                Id = p.Id,
                StationId = p.StationId,
                FuelTypeId = p.FuelTypeId,
                FuelName = p.FuelName,
                Liters = p.Liters,
                SupplierPricePerLiter = p.SupplierPricePerLiter,
                MarginUahPerLiter = p.MarginUahPerLiter,
                MarginPercent = p.MarginPercent,
                FinalPricePerLiter = p.FinalPricePerLiter,
                PriceUpdatedAt = p.PriceUpdatedAt,
                PriceUpdatedByUserId = p.PriceUpdatedByUserId,
            })
            .ToListAsync(ct);

        // Enrich with station names via a lookup
        var stationIds = packages.Select(p => p.StationId).Distinct().ToList();
        var stations = await _context.Stations
            .AsNoTracking()
            .Where(s => stationIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        foreach (var p in packages)
            p.StationName = stations.TryGetValue(p.StationId, out var name) ? name : p.StationId;

        return packages;
    }
}

public sealed class FuelPriceDto
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string StationName { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
    public DateTime? PriceUpdatedAt { get; set; }
    public Guid? PriceUpdatedByUserId { get; set; }
}
