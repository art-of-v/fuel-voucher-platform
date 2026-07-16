using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPackageSuggestions;

public sealed class GetPackageSuggestionsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPackageSuggestionsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PackageSuggestionDto>> HandleAsync(GetPackageSuggestionsQuery query, CancellationToken ct = default)
    {
        var allVouchers = await _context.FuelVouchers.AsNoTracking().ToListAsync(ct);
        var uniqueCombos = allVouchers
            .GroupBy(v => new { v.Provider, v.FuelTypeId, v.Liters })
            .Select(g => g.First())
            .ToList();

        var allStations = await _context.Stations.AsNoTracking().ToListAsync(ct);
        var allFuelTypes = await _context.FuelTypes.AsNoTracking().ToListAsync(ct);
        var existingPackages = await _context.FuelPackages.AsNoTracking().ToListAsync(ct);

        var suggestions = new List<PackageSuggestionDto>();

        foreach (var voucher in uniqueCombos)
        {
            var cleanProvider = voucher.Provider.ToLowerInvariant().Trim();
            var station = allStations.FirstOrDefault(s => s.Name.ToLowerInvariant().Trim() == cleanProvider);
            if (station is null) continue;

            var fuelType = allFuelTypes.FirstOrDefault(ft =>
                ft.StationId == station.Id && ft.Id == voucher.FuelTypeId);
            if (fuelType is null) continue;

            var exists = existingPackages.Any(pkg =>
                pkg.StationId == station.Id &&
                pkg.FuelTypeId == fuelType.Id &&
                pkg.Liters == (int)voucher.Liters);
            if (exists) continue;

            var generatedId = $"{station.Id}-{fuelType.Name.Replace(" ", "").ToLowerInvariant()}-{voucher.Liters}";
            suggestions.Add(new PackageSuggestionDto
            {
                SuggestedId = generatedId,
                StationId = station.Id,
                StationName = station.Name,
                FuelTypeId = fuelType.Id,
                FuelName = fuelType.Name,
                Liters = (int)voucher.Liters
            });
        }

        return suggestions;
    }
}

public sealed class PackageSuggestionDto
{
    public string SuggestedId { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string StationName { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public int Liters { get; set; }
}
