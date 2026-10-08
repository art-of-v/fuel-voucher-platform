using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// Given a freshly-imported voucher batch (the new provider PDF), returns per-fuel context the admin
/// UI needs to pre-fill the cost/liter for the exchange: the current blended cost of the EXISTING
/// stock for that fuel (the "old" cost — the new import carries no batch cost yet, so it is excluded
/// from the blend), the total new liters, and any cost already entered for this import. The UI
/// pre-fills cost/liter = oldBlended + доплата/liter and lets the operator override (planning #104).
/// </summary>
public sealed class GetVoucherExchangeCostContextQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;

    public GetVoucherExchangeCostContextQueryHandler(ApplicationDbContext context, BlendedCostRecalculator recalculator)
    {
        _context = context;
        _recalculator = recalculator;
    }

    public async Task<VoucherExchangeCostContextResponse> HandleAsync(Guid importId, CancellationToken ct = default)
    {
        var news = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.ImportJobId == importId
                && !v.IsDeleted
                && (v.Status == VoucherStatus.Imported || v.Status == VoucherStatus.VerifiedWithWarnings))
            .Select(v => new
            {
                v.FuelTypeId,
                v.Provider,
                v.Liters,
                FuelName = v.FuelType != null ? v.FuelType.Name : null
            })
            .ToListAsync(ct);

        var fuels = new List<VoucherExchangeCostContextFuel>();
        foreach (var group in news.GroupBy(n => n.FuelTypeId))
        {
            var blended = await _recalculator.ComputeBlendedAsync(group.Key, ct);

            // Cost already recorded on this import's own vouchers (null until it is entered); cost lives on
            // the vouchers, so a partly-costed import reports the mean of the part that is.
            var existing = await _context.FuelVouchers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => v.ImportJobId == importId && v.FuelTypeId == group.Key && v.CostPerLiter != null)
                .Select(v => new { v.Liters, v.CostPerLiter!.Value })
                .ToListAsync(ct);
            var existingCostedLiters = existing.Sum(v => v.Liters);

            fuels.Add(new VoucherExchangeCostContextFuel
            {
                FuelTypeId = group.Key,
                FuelName = group.Select(g => g.FuelName).FirstOrDefault(n => n != null),
                Provider = group.Select(g => g.Provider).First(),
                NewCount = group.Count(),
                NewLiters = group.Sum(g => g.Liters),
                OldBlendedCostPerLiter = blended,
                ExistingBatchCostPerLiter = existingCostedLiters > 0m
                    ? existing.Sum(v => v.Liters * v.Value) / existingCostedLiters
                    : null
            });
        }

        return new VoucherExchangeCostContextResponse
        {
            ImportId = importId,
            Fuels = fuels.OrderBy(f => f.FuelTypeId).ToList()
        };
    }
}

public sealed class VoucherExchangeCostContextResponse
{
    public Guid ImportId { get; set; }
    public List<VoucherExchangeCostContextFuel> Fuels { get; set; } = new();
}

public sealed class VoucherExchangeCostContextFuel
{
    public string FuelTypeId { get; set; } = string.Empty;
    public string? FuelName { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int NewCount { get; set; }
    public decimal NewLiters { get; set; }

    /// <summary>Blended cost/liter of the EXISTING stock for this fuel (the "old" cost), or null if none.</summary>
    public decimal? OldBlendedCostPerLiter { get; set; }

    /// <summary>Cost/liter already recorded for this import × fuel, if the admin set it earlier.</summary>
    public decimal? ExistingBatchCostPerLiter { get; set; }
}
