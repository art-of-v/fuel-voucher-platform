using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.API.BackgroundJobs;

public partial class FulfillmentService
{

    /// <summary>
    /// Finds open renewal orders (at least one still-unfulfilled item) and re-runs their fulfilment.
    /// Folded into <see cref="ProcessPendingOrdersAsync"/> so the per-minute API job is the safety
    /// net when a webhook enqueue was lost or stock only arrived later.
    /// </summary>
    private async Task ProcessOpenRenewalOrdersBackfillAsync(CancellationToken cancellationToken)
    {
// "Has a renewal line we have not delivered yet" - the same question the old
        // `VoucherRenewalItems.Any(i => i.FulfilledVoucherId == null)` asked, asked of the line and
        // its fulfilment instead. NOT negated by the global released-filter: a released fulfilment
        // still delivered the term, so the line is not owed again.
        var openRenewalOrderIds = await _context.Orders
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                         && o.Kind == OrderKind.Renewal
                         && _context.OrderLineItems
                             .Where(l => l.OrderId == o.Id && l.SourceVoucherId != null)
                             .Any(l => !_context.Fulfillments
                                 .IncludingReleasedFulfilments()
                                 .Any(f => f.LineItemId == l.Id)))
            .OrderBy(o => o.CreatedAtUtc)
            .Select(o => o.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (openRenewalOrderIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Backfilling {Count} open renewal orders", openRenewalOrderIds.Count);

        foreach (var orderId in openRenewalOrderIds)
        {
            try
            {
                await ProcessRenewalOrderAsync(orderId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill renewal order {OrderId}", orderId);
            }
        }
    }


    private async Task<FuelVoucher?> FindReplacementVoucherAsync(
        FuelVoucher source,
        DateOnly promisedExpiration,
        List<Guid> usedStockIds,
        bool isTestAccount,
        CancellationToken cancellationToken)
    {
        // Same brand/fuel/nominal as the source, valid at least until the promise we owe the
        // customer (their leftover term + what they just paid for), not already claimed in this run.
        // Oldest-eligible first, so the SHORTEST qualifying voucher goes out and Fuel Flow's near-expiry
        // stock is preserved for the tiers that actually need it — a shorter voucher that still covers the
        // promise is strictly better than a longer one. A QA account draws only from test stock and a real
        // account only from real stock, re-asserted atomically at the claim (TryAssignReplacementVoucherAsync).
        // AsNoTracking for the same reason as FindMatchingVoucherAsync (#246): a decision whose row is
        // claimed by a raw UPDATE two lines later, never a write in its own right.
        var pool = DataKinds.ForPool(isTestAccount);

        return await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Brand == source.Brand
                     && v.FuelTypeId == source.FuelTypeId
                     && v.Liters == source.Liters
                     && v.DataKind == pool
                     && v.SupplierExpirationDate >= promisedExpiration
                     && !usedStockIds.Contains(v.Id))
            .OrderBy(v => v.SupplierExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Atomic extension with compare-and-set: moves the customer expiry to <paramref name="newExpiration"/>
    /// only while the row still holds the dates we priced against AND the new date still fits inside the
    /// supplier's real term. Returns rows affected (0 or 1), so 0 means "someone beat us, or the ceiling
    /// moved" rather than a silent success.
    /// </summary>
    protected internal virtual async Task<int> TryExtendVoucherAsync(
        Guid voucherId,
        Guid userId,
        DateOnly expectedCustomerExpiration,
        DateOnly providerExpiration,
        DateOnly newExpiration,
        CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET customer_expiration_date = {newExpiration}, updated_at_utc = {SystemClock.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned' AND customer_expiration_date = {expectedCustomerExpiration} AND {newExpiration} <= supplier_expiration_date""",
            cancellationToken);
    }

    /// <summary>Atomic replacement claim: assign an Available voucher to the customer only if it is
    /// still valid at least until <paramref name="promisedExpiration"/> (the source's leftover term plus the
    /// term just paid for). The expiry gate is
    /// re-checked here, not just at select time, so an admin edit between SELECT and claim cannot
    /// hand out an under-term voucher. Returns rows affected (0 or 1).</summary>
    protected internal virtual async Task<int> TryAssignReplacementVoucherAsync(
        Guid voucherId, Guid userId, Guid? legalEntityId, DateOnly promisedExpiration, Guid orderId, CancellationToken cancellationToken)
    {
        // The renewal order owns the replacement, so order_id is written in the same atomic claim
        // as the assignment rather than inferred later from the Fulfillment row.
        //
        // customer_expiration_date is stamped with the PROMISE (what the customer paid for on top of
        // what they already held), not the voucher's own customer date. The voucher is chosen with
        // supplier_expiration_date >= promise, so the promise always fits and never needs clamping —
        // whatever real life is left above it is a hidden reserve the customer can extend again.
        //
        // Same QA/test segregation as the buy claim: the replacement must come from the owner's pool
        // (test for a QA account, real otherwise), re-asserted atomically here against the owning user.
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, customer_expiration_date = {promisedExpiration}, updated_at_utc = {SystemClock.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {promisedExpiration} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
            cancellationToken);
    }

    /// <summary>
    /// Puts a replaced customer's voucher back into the sellable pool. The customer is done with it — we
    /// issued them a different voucher — so ownership, the legal-entity stamp and any worker hand-off are
    /// cleared and the status returns to <c>Available</c>: it is Fuel Flow's stock again, and the next
    /// customer to claim it (possibly the same one) pays for it again, so there is no double revenue.
    /// <para>
    /// Its <c>order_id</c> is deliberately KEPT, still pointing at the purchase that delivered it. That link
    /// records where this paper originally came from, and re-homing it to the renewal would rewrite that
    /// (pinned by <c>VoucherOrderOwnershipIntegrationTests</c>). It is also harmless as stock: the wallet
    /// reads vouchers through <c>Fulfillments</c>, and the next claim overwrites it anyway.
    /// </para>
    /// <para>
    /// The <see cref="Fulfillment"/> row — and through its LineItemId, the renewal line that bought the
    /// replacement — is kept too: together they are the audit chain that still explains where this
    /// voucher went before. What must NOT happen is the previous customer still seeing it —
    /// <c>GetUserPurchases</c> only returns vouchers the caller actually owns, which is what keeps a
    /// recycled voucher out of the wallet it came from.
    /// </para>
    /// <para>
    /// It does re-enter the operator's «Заміна талонів» attention list once it nears expiry, which is right:
    /// it is stock that is about to lapse, and swapping it with the supplier is one way to stop that.
    /// </para>
    /// </summary>
    protected internal virtual async Task<int> TryReleaseReplacedVoucherAsync(
        Guid voucherId, Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Available', assigned_to_user_id = NULL, legal_entity_id = NULL, worker_user_id = NULL, updated_at_utc = {SystemClock.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned'""",
            cancellationToken);
    }
}
