using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.Features.Orders.SharedServices;

/// <summary>
/// Resolves which order a voucher belongs to once the fuel leaves a worker and goes back to the
/// company.
///
/// While a worker holds it, a voucher belongs to the ISSUANCE order - that is what tells the worker
/// where their fuel came from. Once it is recalled, or the worker is let go, the fuel is company fuel
/// again and must sit back under the PURCHASE it arrived with, otherwise the company's own history
/// would keep claiming fuel it no longer owns.
///
/// Resolution is "earliest fulfillment wins", the same rule the O1 migration used to backfill
/// <c>fuel_vouchers.order_id</c>. That matters for a voucher whose order is not an issuance: the
/// earliest fulfillment is the purchase that originally delivered it, which is where a renewed voucher
/// belongs too.
/// </summary>
public static class IssuanceOrderLink
{
    /// <summary>
    /// Returns, per voucher, the order it should belong to after leaving a worker. A voucher that is
    /// not currently under an issuance order keeps its own order untouched, so this is safe to call on
    /// any set of vouchers.
    /// </summary>
    public static async Task<Dictionary<Guid, Guid>> RestoreToPurchasesAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid> voucherIds,
        CancellationToken cancellationToken)
    {
        if (voucherIds.Count == 0)
            return [];

        var ids = voucherIds.Distinct().ToList();

        var current = await context.FuelVouchers
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.OrderId })
            .ToListAsync(cancellationToken);

        var issuanceOrderIds = current
            .Where(x => x.OrderId is not null)
            .Select(x => x.OrderId!.Value)
            .Distinct()
            .ToList();

        var issuances = await context.Orders
            .AsNoTracking()
            .Where(o => issuanceOrderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Kind, o.SourceOrderId })
            .ToListAsync(cancellationToken);

        var issuanceById = issuances.ToDictionary(o => o.Id);

        // Only vouchers whose current order IS an issuance need to move. Loading the original
        // delivery is deferred until we know we need it, so the common "nothing to do" path stays
        // at two queries.
        var vouchersToRepoint = current
            .Where(x => x.OrderId is { } orderId
                        && issuanceById.TryGetValue(orderId, out var issuance)
                        && issuance.Kind == OrderKind.ReceivedFromCompany)
            .Select(x => x.Id)
            .ToList();

        var purchaseByVoucher = new Dictionary<Guid, Guid>();

        if (vouchersToRepoint.Count > 0)
        {
            // An issuance normally names the purchase it drew from, but a batch may span several
            // purchases and then have no single parent to name. Fall back to the voucher's own first
            // delivery, which is always there: these vouchers arrived at the company under an order.
            var fulfilled = await context.Fulfillments
                .AsNoTracking()
                .Where(f => vouchersToRepoint.Contains(f.VoucherId))
                .OrderBy(f => f.FulfilledAtUtc)
                .ThenBy(f => f.OrderId)
                .Select(f => new { f.VoucherId, f.OrderId })
                .ToListAsync(cancellationToken);

            foreach (var row in fulfilled)
            {
                if (!purchaseByVoucher.ContainsKey(row.VoucherId))
                    purchaseByVoucher[row.VoucherId] = row.OrderId;
            }
        }

        var resolved = new Dictionary<Guid, Guid>(current.Count);

        foreach (var voucher in current)
        {
            if (voucher.OrderId is { } orderId
                && issuanceById.TryGetValue(orderId, out var issuance)
                && issuance.Kind == OrderKind.ReceivedFromCompany)
            {
                // SourceOrderId when the issuance named one, otherwise the voucher's own first
                // delivery. If neither can be found we keep the issuance order rather than null it:
                // these vouchers are still Assigned/Blocked, and the ck_voucher_held_has_order
                // constraint would reject a null.
                var restored = issuance.SourceOrderId
                    ?? (purchaseByVoucher.TryGetValue(voucher.Id, out var purchase) ? (Guid?)purchase : null)
                    ?? orderId;
                resolved[voucher.Id] = restored;
                continue;
            }

            if (voucher.OrderId is { } unchanged)
                resolved[voucher.Id] = unchanged;
        }

        return resolved;
    }
}