using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPackageSuggestions;

public sealed class GetPackageSuggestionsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPackageSuggestionsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PackageSuggestionDto>> HandleAsync(GetPackageSuggestionsQuery query, CancellationToken ct = default)
    {
        var allVouchers = await _context.FuelVouchers.AsNoTracking().ToListAsync(ct);
        var uniqueCombos = allVouchers
            .GroupBy(v => new { v.Brand, v.FuelTypeId, v.Liters })
            .Select(g => g.First())
            .ToList();

        var allBrands = await _context.Brands.AsNoTracking().ToListAsync(ct);
        var allFuelTypes = await _context.FuelTypes.AsNoTracking().ToListAsync(ct);
        // Only "does this triple already exist" is asked, so the existence set is projected down to the
        // triple rather than to a whole sellable package: the card this feeds does not need the price,
        // and loading every per-litre column of every package to answer a yes/no is how this endpoint
        // grew into reading the whole catalogue.
        var existingPackages = await _context.FuelOfferVolumes
            .AsNoTracking()
            .Select(v => new { v.Offer.BrandId, v.Offer.FuelTypeId, v.Liters })
            .ToListAsync(ct);

        var suggestions = new List<PackageSuggestionDto>();

        foreach (var voucher in uniqueCombos)
        {
            // Vouchers carry the brand NAME; the catalogue carries a slug, and the two have always
            // been the same string in a different case (see StockKey). Matching on the slug rather than
            // the name is what lets a network keep a code that differs from its display name.
            var slug = voucher.Brand.ToLowerInvariant().Trim();
            var Brand = allBrands.FirstOrDefault(s => s.Slug.ToLowerInvariant().Trim() == slug);
            if (Brand is null) continue;

            var fuelType = allFuelTypes.FirstOrDefault(ft =>
                ft.BrandId == Brand.Id && ft.Id == voucher.FuelTypeId);
            if (fuelType is null) continue;

            var exists = existingPackages.Any(pkg =>
                pkg.BrandId == Brand.Id &&
                pkg.FuelTypeId == fuelType.Id &&
                pkg.Liters == voucher.Liters);
            if (exists) continue;

            var generatedId = $"{Brand.Slug}-{fuelType.Name.Replace(" ", "").ToLowerInvariant()}-{voucher.Liters}";
            suggestions.Add(new PackageSuggestionDto
            {
                SuggestedId = generatedId,
                BrandId = Brand.Id,
                BrandName = Brand.Name,
                FuelTypeId = fuelType.Id,
                FuelName = fuelType.Name,
                Liters = voucher.Liters
            });
        }

        return suggestions;
    }
}

public sealed class PackageSuggestionDto
{
    public string SuggestedId { get; set; } = null!;
    public Guid BrandId { get; set; }
    public string BrandName { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
}
