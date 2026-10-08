using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>Per-fuel batch P&amp;L rollup for one import (pricing epic slice 2b): liters in/sold/remaining,
/// realized revenue &amp; margin from sold vouchers, and unrealized margin sitting in remaining stock.</summary>
public sealed record GetImportBatchPnlQuery(Guid ImportId);

public sealed class GetImportBatchPnlQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetImportBatchPnlQueryHandler(ApplicationDbContext context) => _context = context;

    // Owned-but-unsold stock (mirrors BlendedCostRecalculator's pool).
    private static readonly VoucherStatus[] InStockStatuses =
        [VoucherStatus.Imported, VoucherStatus.VerifiedWithWarnings, VoucherStatus.Available];

    // Orders whose revenue is fully reversed — a sold voucher on one of these earns nothing.
    // Partial refunds are counted gross in v1 (see #86); exact per-unit netting is a follow-up.
    private static readonly OrderStatus[] ReversedStatuses =
        [OrderStatus.Refunded, OrderStatus.Cancelled];

    public async Task<List<ImportBatchPnlDto>> HandleAsync(GetImportBatchPnlQuery query, CancellationToken cancellationToken = default)
    {
        var vouchers = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.ImportJobId == query.ImportId && !v.IsDeleted)
            .Select(v => new { v.Id, v.FuelTypeId, v.Provider, v.Liters, v.Status, v.CostPerLiter })
            .ToListAsync(cancellationToken);

        if (vouchers.Count == 0) return [];

        var voucherIds = vouchers.Select(v => v.Id).ToList();
        var fuelIds = vouchers.Select(v => v.FuelTypeId).Distinct().ToList();

        // Fulfillments tie a sold voucher to the order that bought it.
        var fulfillments = await _context.Fulfillments
            .AsNoTracking()
            .Where(f => voucherIds.Contains(f.VoucherId))
            .Select(f => new { f.VoucherId, f.OrderId })
            .ToListAsync(cancellationToken);

        var orderIds = fulfillments.Select(f => f.OrderId).Distinct().ToList();

        var orderStatus = await _context.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => (Status: o.Status, Kind: o.Kind), cancellationToken);

        // Sale price lives on the line item, keyed within an order by (fuel, liters) — FuelTypeId already
        // encodes supplier+fuel, so it uniquely maps a voucher to its line item's per-voucher UnitPrice.
        var lineItems = await _context.OrderLineItems
            .AsNoTracking()
            .Where(li => orderIds.Contains(li.OrderId))
            .Select(li => new { li.OrderId, li.FuelTypeId, li.Liters, li.UnitPrice })
            .ToListAsync(cancellationToken);
        var unitPriceByLine = lineItems
            .GroupBy(li => (li.OrderId, li.FuelTypeId, li.Liters))
            .ToDictionary(g => g.Key, g => g.First().UnitPrice);

        var names = await _context.FuelTypes
            .AsNoTracking()
            .Where(f => fuelIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        // Current sale price/L per fuel (packages of the same fuel share the per-liter final).
        var currentPrices = (await _context.FuelPackages
            .AsNoTracking()
            .Where(p => fuelIds.Contains(p.FuelTypeId) && p.FinalPricePerLiter != null)
            .Select(p => new { p.FuelTypeId, p.FinalPricePerLiter })
            .ToListAsync(cancellationToken))
            .GroupBy(p => p.FuelTypeId)
            .ToDictionary(g => g.Key, g => g.First().FinalPricePerLiter);

        var voucherById = vouchers.ToDictionary(v => v.Id);

        // Vouchers with at least one non-reversed fulfillment = revenue realised. Used to keep
        // sold-then-expired vouchers out of the operator loss bucket. A company handover counts as
        // "sold" ON PURPOSE, the opposite of the revenue loop below: the fuel left the operator's
        // hands, so if it then expires that is not an operator loss — the company bought it.
        var soldVoucherIds = fulfillments
            .Where(f => !(orderStatus.TryGetValue(f.OrderId, out var sold) && ReversedStatuses.Contains(sold.Status)))
            .Select(f => f.VoucherId)
            .ToHashSet();

        var result = new List<ImportBatchPnlDto>();
        foreach (var group in vouchers
            .GroupBy(v => new { v.FuelTypeId, v.Provider })
            .OrderBy(g => g.Key.FuelTypeId))
        {
            var fuelTypeId = group.Key.FuelTypeId;
            var all = group.ToList();

            var remaining = all.Where(v => InStockStatuses.Contains(v.Status)).ToList();

            // Operator stock that lapsed unsold (retired to Expired by the slice-4 job). Sold-then-expired
            // vouchers are excluded — their revenue was realised and their expiry is the customer's, not a loss.
            var expired = all.Where(v => v.Status == VoucherStatus.Expired && !soldVoucherIds.Contains(v.Id)).ToList();
            var litersExpired = expired.Sum(v => v.Liters);

            var vouchersSold = 0;
            var litersSold = 0m;
            var revenue = 0m;
            foreach (var f in fulfillments)
            {
                if (!voucherById.TryGetValue(f.VoucherId, out var v) || v.FuelTypeId != fuelTypeId) continue;
                if (orderStatus.TryGetValue(f.OrderId, out var st) && ReversedStatuses.Contains(st.Status)) continue;
                // A company handover moves fuel the company already bought, so counting it here would
                // book the same litre as sold twice — once against the company's purchase, once here.
                // No revenue, no COGS, no "sold" count from a handover. (Note the deliberate contrast
                // with soldVoucherIds above: a handover is not a sale, but it IS not an operator loss.)
                if (orderStatus.TryGetValue(f.OrderId, out var kind) && kind.Kind == OrderKind.ReceivedFromCompany) continue;
                vouchersSold++;
                litersSold += v.Liters;
                if (unitPriceByLine.TryGetValue((f.OrderId, v.FuelTypeId, v.Liters), out var price)) revenue += price;
            }

            var currentPrice = currentPrices.TryGetValue(fuelTypeId, out var cp) ? cp : null;

            // Cost is per voucher, not per batch: a customer who extends one voucher of a batch pays for
            // exactly that voucher, so its cost drops while its siblings keep theirs. Every money figure
            // below is therefore summed per voucher over the vouchers it actually concerns, and the
            // batch's reported CostPerLiter is their liters-weighted mean — the batch figure is a
            // rollup for display, never an input to the sums.
// COGS is booked on exactly the vouchers counted as sold below — same reversal filter, otherwise a
            // refunded voucher's liters would be expensed while its revenue is excluded.
            var soldIds = fulfillments
                .Where(f => voucherById.TryGetValue(f.VoucherId, out var fv) && fv.FuelTypeId == fuelTypeId)
                .Where(f => !(orderStatus.TryGetValue(f.OrderId, out var st) && ReversedStatuses.Contains(st.Status)))
                .Where(f => !(orderStatus.TryGetValue(f.OrderId, out var kind) && kind.Kind == OrderKind.ReceivedFromCompany))
                .Select(f => f.VoucherId)
                .ToHashSet();

            var batchCost = all.Any(v => v.CostPerLiter.HasValue)
                ? PurchaseBatchCosting.BlendedCostPerLiter(
                    all.Where(v => v.CostPerLiter.HasValue).Select(v => (v.Liters, v.CostPerLiter!.Value)))
                : null;

            decimal? realizedCogs = null, realizedMargin = null, unrealizedMargin = null;
            decimal? expiredLoss = null, netRealizedResult = null;
            // Null means "this batch is not costed, so the figures are unknown" — a computed 0 means the
            // batch IS costed and simply has nothing sold or expired yet. The sums inside run per voucher
            // over its own cost, so a voucher a customer extended is no longer valued at its purchase price.
            if (batchCost.HasValue)
            {
                realizedCogs = all
                    .Where(v => soldIds.Contains(v.Id) && v.CostPerLiter.HasValue)
                    .Sum(v => v.Liters * v.CostPerLiter!.Value);
                realizedMargin = revenue - realizedCogs.Value;

                if (currentPrice.HasValue)
                {
                    var remainingCosted = remaining.Where(v => v.CostPerLiter.HasValue).ToList();
                    if (remainingCosted.Count > 0)
                        unrealizedMargin = remainingCosted.Sum(v => v.Liters * (currentPrice.Value - v.CostPerLiter!.Value));
                }

                expiredLoss = expired
                    .Where(v => v.CostPerLiter.HasValue)
                    .Sum(v => v.Liters * v.CostPerLiter!.Value);

                netRealizedResult = realizedMargin.Value - expiredLoss.Value;
            }

            result.Add(new ImportBatchPnlDto
            {
                FuelTypeId = fuelTypeId,
                FuelTypeName = names.TryGetValue(fuelTypeId, out var n) ? n : null,
                Provider = group.Key.Provider,
                VouchersIn = all.Count,
                LitersIn = all.Sum(v => v.Liters),
                VouchersSold = vouchersSold,
                LitersSold = litersSold,
                VouchersRemaining = remaining.Count,
                LitersRemaining = remaining.Sum(v => v.Liters),
                VouchersExpired = expired.Count,
                LitersExpired = litersExpired,
                CostPerLiter = batchCost,
                RealizedRevenue = revenue,
                RealizedCogs = realizedCogs,
                RealizedMargin = realizedMargin,
                AvgSalePricePerLiter = litersSold > 0 ? revenue / litersSold : null,
                CurrentPricePerLiter = currentPrice,
                UnrealizedMargin = unrealizedMargin,
                ExpiredLoss = expiredLoss,
                NetRealizedResult = netRealizedResult,
            });
        }

        return result;
    }
}
