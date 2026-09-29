using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>Per-fuel batch-cost rollup for one import: what fuels it holds, their entered cost, and the live blended cost.</summary>
public sealed record GetImportBatchCostsQuery(Guid ImportId);

public sealed class GetImportBatchCostsQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;

    public GetImportBatchCostsQueryHandler(ApplicationDbContext context, BlendedCostRecalculator recalculator)
    {
        _context = context;
        _recalculator = recalculator;
    }

    public async Task<List<ImportBatchCostDto>> HandleAsync(GetImportBatchCostsQuery query, CancellationToken cancellationToken = default)
    {
        // Roll the import's vouchers up per fuel (mirrors the vouchers tab: IgnoreQueryFilters).
        var groups = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.ImportJobId == query.ImportId)
            .GroupBy(v => new { v.FuelTypeId, v.Provider })
            .Select(g => new
            {
                g.Key.FuelTypeId,
                g.Key.Provider,
                VoucherCount = g.Count(),
                TotalLiters = g.Sum(v => v.Liters)
            })
            .ToListAsync(cancellationToken);

        if (groups.Count == 0) return [];

        var fuelIds = groups.Select(g => g.FuelTypeId).Distinct().ToList();

        var names = await _context.FuelTypes
            .AsNoTracking()
            .Where(f => fuelIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        var costs = await _context.PurchaseBatches
            .AsNoTracking()
            .Where(b => b.ImportJobId == query.ImportId && fuelIds.Contains(b.FuelTypeId))
            .ToDictionaryAsync(b => b.FuelTypeId, b => b.CostPerLiter, cancellationToken);

        var result = new List<ImportBatchCostDto>();
        foreach (var g in groups.OrderBy(g => g.FuelTypeId))
        {
            var blended = await _recalculator.ComputeBlendedAsync(g.FuelTypeId, cancellationToken);
            result.Add(new ImportBatchCostDto
            {
                FuelTypeId = g.FuelTypeId,
                FuelTypeName = names.TryGetValue(g.FuelTypeId, out var name) ? name : null,
                Provider = g.Provider,
                VoucherCount = g.VoucherCount,
                TotalLiters = g.TotalLiters,
                CostPerLiter = costs.TryGetValue(g.FuelTypeId, out var c) ? c : null,
                BlendedCostPerLiter = blended
            });
        }

        return result;
    }
}
