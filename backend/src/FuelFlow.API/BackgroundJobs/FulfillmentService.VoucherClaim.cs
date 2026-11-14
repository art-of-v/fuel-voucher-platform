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

    private async Task<List<FuelVoucher>> FindMatchingVouchersAsync(
        Order order,
        OrderLineItem lineItem,
        List<Guid> usedVoucherIds,
        bool isTestAccount,
        int take,
        CancellationToken cancellationToken)
    {
        // Expiry is enforced, not optional. With this filter commented out, the ascending
        // brand-term sort handed every paying customer the *most* expired stock first, and
        // nothing in the system ever flips stale rows to Expired (that is a manual admin
        // action), so the oldest unredeemable voucher was permanently at the head of the queue.
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);

        // TODO: temporary testing escape hatch — remove once the import workflow no longer
        // needs to run against expired test stock. Only ever flip it in a disposable/staging
        // environment; turning it off in production hands the most-expired stock to customers.
        var expiryEnabled = _configuration.GetValue<bool>("VoucherExpiration:Enabled", true);
        var pool = DataKinds.ForPool(isTestAccount);

        // A QA account draws only from test stock and a real account only from real stock, so a seeded
        // test voucher never reaches a paying customer and a QA run never burns real inventory.
        // Re-asserted atomically at the claim (TryAssignVoucherAsync).
        // AsNoTracking (#246): this is a decision, not a write. The chosen row is claimed by a raw
        // compare-and-swap UPDATE moments later, and tracking it here would put an instance with the
        // pre-claim xmin into the change tracker — where any later save of that row would fail on a
        // conflict that never happened.
        return await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Brand == lineItem.Brand
                     && v.FuelTypeId == lineItem.FuelTypeId
                     && v.Liters == lineItem.Liters
                     && v.DataKind == pool
                     && (!expiryEnabled || v.SupplierExpirationDate >= today)
                     && !usedVoucherIds.Contains(v.Id))
            .OrderBy(v => v.SupplierExpirationDate)
            // The units of one line take different vouchers, and they take them in this order.
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The customer-facing expiry for a line bought on a term: today plus that term. Null when the line
    /// carries no term, which means "sell the voucher's full remaining life" — the behaviour for every
    /// order placed before short terms existed.
    /// </summary>
    /// <remarks>
    /// A floor on the promise, not a gate on stock. A line asking for more life than the cheapest matching
    /// voucher has is still fulfilled; <see cref="TryAssignVoucherAsync"/> clamps the written date to the
    /// voucher's own supplier term, so the customer gets exactly what the supplier backs and never more.
    /// Refusing the sale here instead would strand a paid order over a stock-mix detail.
    /// </remarks>
    private static DateOnly? CustomerExpirationFor(OrderLineItem lineItem)
        => VoucherRenewalTerms.TryFromCode(lineItem.TermCode, out var term)
            ? term.ApplyTo(DateOnly.FromDateTime(SystemClock.UtcNow))
            : null;

    protected internal virtual async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "orders" SET status = 'Fulfilled', fulfilled_at_utc = {SystemClock.UtcNow}, updated_at_utc = {SystemClock.UtcNow}, partially_fulfilled_since_utc = NULL WHERE id = {orderId} AND (status = 'PendingFulfillment' OR status = 'PartiallyFulfilled')""",
            cancellationToken);

        return rowsAffected;
    }

    protected internal virtual async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, Guid orderId, DateOnly? customerExpiration, CancellationToken cancellationToken)
    {
        // The expiry predicate is repeated here on purpose: this UPDATE is the atomic claim, and
        // between the SELECT that chose this voucher and this statement the date can roll over
        // or an admin can edit the row. The claim itself must refuse expired stock.
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        var expiryEnabled = _configuration.GetValue<bool>("VoucherExpiration:Enabled", true);
        var expiryCutoff = expiryEnabled ? today : DateOnly.MinValue;

        // order_id lands in this same statement on purpose: this is the atomic claim, so the
        // voucher must never be in someone's hands while its owning order is still unknown, not
        // even for the few statements before the Fulfillment row is inserted below.
        //
        // The shortened customer expiry rides on the same statement, deliberately. Un-assignment
        // (FixMismatchedFulfillmentsAsync) puts the voucher back to Available but does not restore a
        // date, so a shortened date written by a separate statement would survive the rollback and the
        // stock would silently re-enter the pool with less life than the supplier granted.
        //
        // The supplier term is the ceiling, so the promise is clamped rather than trusted: a line whose
        // term outruns the voucher gets the voucher's full remaining life, which is what it would have
        // got before short terms existed.
        //
        // The QA/test pools stay separate at the atomic claim too: a QA account can only claim test
        // stock and a real account only real stock. A correlated subquery on the owning user keeps the
        // check race-proof, exactly like the expiry predicate above, so a flag edit between the SELECT
        // and this statement can never hand stock across the pools.
        var rowsAffected = customerExpiration is null
            ? await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, updated_at_utc = {SystemClock.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {expiryCutoff} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
                cancellationToken)
            : await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, customer_expiration_date = LEAST({customerExpiration.Value}, supplier_expiration_date), updated_at_utc = {SystemClock.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND supplier_expiration_date >= {expiryCutoff} AND data_kind = (SELECT CASE WHEN u.is_qa_account THEN 'qa' ELSE 'real' END FROM "users" u WHERE u.id = {userId})""",
                cancellationToken);

        return rowsAffected;
    }
}
