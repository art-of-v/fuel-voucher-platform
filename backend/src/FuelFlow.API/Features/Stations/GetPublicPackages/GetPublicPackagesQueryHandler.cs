using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackages;

public sealed class GetPublicPackagesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicPackagesQueryHandler(ApplicationDbContext context) => _context = context;

    // Projects PublicPackageResponse (store-front fields only) — never the raw FuelPackage entity,
    // which carries the operator's supplier cost + margin. See PublicPackageResponse (planning #52).
    // OriginalPrice (the struck-through "before" price) is recomputed fresh from the pump price via
    // FuelPricing.OriginalPackagePrice (planning #73), so it is the public list/pump price — never the
    // leaked supplier cost — and every legacy row is corrected with no data migration. The SQL still
    // selects only store-front columns + pump (a public list number); cost/margin never leave the DB.
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesQuery query, CancellationToken ct = default)
    {
        var rows = await _context.FuelPackages
            .AsNoTracking()
            // Rehearsal packages are real rows and stay visible to the admin screens, but this
            // endpoint is anonymous and is what the app and the marketing site read. A test package
            // reaching it is purchasable - which is exactly how "QA-CHECK-95", 5 L at 1 UAH, ended
            // up on sale in production. Filtered here rather than by a global query filter so the
            // admin can still see and fix the row.
            .Where(x => !x.IsTestData)
            .OrderBy(x => x.StationId)
            .ThenBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .Select(x => new
            {
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                x.PumpPricePerLiter,
                x.FinalPricePerLiter,
            })
            .ToListAsync(ct);

        return rows
            .Select(x => new PublicPackageResponse(
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, x.Price),
                x.FinalPricePerLiter))
            .ToList();
    }
}
