using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.API.BackgroundJobs;

public class FulfillmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FulfillmentService> _logger;
    private readonly RefundOrderCommandHandler _refundHandler;
    private readonly RuntimeSettingsService _settings;
    private readonly NotificationDispatcher _notifications;
    private readonly IConfiguration _configuration;

    public FulfillmentService(
        ApplicationDbContext context,
        ILogger<FulfillmentService> logger,
        RefundOrderCommandHandler refundHandler,
        RuntimeSettingsService settings,
        NotificationDispatcher notifications,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _refundHandler = refundHandler;
        _settings = settings;
        _notifications = notifications;
        _configuration = configuration;
    }

    public async Task ProcessPendingOrdersAsync(CancellationToken cancellationToken = default)
    {
        var unprocessedEvents = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.OrderCreated)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (unprocessedEvents.Any())
        {
            _logger.LogInformation("Processing {Count} pending ORDER_CREATED events", unprocessedEvents.Count);

            foreach (var outboxEvent in unprocessedEvents)
            {
                try
                {
                    await ProcessOrderCreatedEventAsync(outboxEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process outbox event {EventId}", outboxEvent.Id);
                }
            }
        }
        else
        {
            _logger.LogDebug("No pending ORDER_CREATED events to process");
        }

        await FixMismatchedFulfillmentsAsync(cancellationToken);
        await ProcessOpenOrdersBackfillAsync(cancellationToken);
        await ProcessOpenRenewalOrdersBackfillAsync(cancellationToken);
    }

    private async Task FixMismatchedFulfillmentsAsync(CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        int skip = 0;
        bool hasMore;

        do
        {
            var fulfilledOrders = await _context.Orders
                .Include(o => o.LineItems)
                // Renewal orders are fulfilled by ProcessRenewalOrderAsync (two-branch: extend the
                // source voucher in place, or replace it from stock). They have no
                // one-stock-voucher-per-unit shape, so this liter-count trimmer must never touch
                // them — it would strip a renewal fulfillment and wrongly flip an extended/source
                // voucher back to Available. Exclude them.
                .Where(o => o.Status == OrderStatus.Fulfilled
                            && !_context.VoucherRenewalItems.Any(i => i.OrderId == o.Id))
                .OrderBy(o => o.Id)
                .Skip(skip)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            hasMore = fulfilledOrders.Count == batchSize;
            skip += batchSize;

            foreach (var order in fulfilledOrders)
            {
                var currentOrder = order;
                IDbContextTransaction? transaction = null;

                try
                {
                    if (_context.Database.IsRelational())
                    {
                        // Serialize trimming per order so it can never race with a concurrent
                        // fulfillment that is assigning vouchers to the same order.
                        transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({currentOrder.Id.ToString()}))",
                            cancellationToken);

                        var freshOrder = await _context.Orders
                            .AsNoTracking()
                            .Include(o => o.LineItems)
                            .FirstOrDefaultAsync(o => o.Id == currentOrder.Id, cancellationToken);

                        if (freshOrder == null || freshOrder.Status != OrderStatus.Fulfilled)
                        {
                            await transaction.CommitAsync(cancellationToken);
                            continue;
                        }

                        currentOrder = freshOrder;
                    }

                    var lineItemCounts = currentOrder.LineItems?
                        .GroupBy(li => li.Liters)
                        .ToDictionary(g => g.Key, g => g.Sum(li => li.Quantity))
                        ?? [];

                    var fulfillments = await _context.Fulfillments
                        .Where(f => f.OrderId == currentOrder.Id)
                        .ToListAsync(cancellationToken);

                    var fulfillmentVoucherIds = fulfillments.Select(f => f.VoucherId).ToList();

                    var vouchers = fulfillmentVoucherIds.Count != 0
                        ? await _context.FuelVouchers
                            .Where(v => fulfillmentVoucherIds.Contains(v.Id))
                            .ToListAsync(cancellationToken)
                        : [];

                    var voucherCounts = vouchers
                        .GroupBy(v => v.Liters)
                        .ToDictionary(g => g.Key, g => g.Count());

                    var hasMismatch = lineItemCounts.Any(kv =>
                        !voucherCounts.TryGetValue(kv.Key, out var count) || count != kv.Value);

                    if (hasMismatch)
                    {
                        _logger.LogInformation(
                            "Fixing mismatched fulfillments for order {OrderId}", currentOrder.Id);

                        var excessVouchers = new List<(Fulfillment Fulfillment, FuelVoucher Voucher)>();

                        foreach (var kv in voucherCounts)
                        {
                            var needed = lineItemCounts.GetValueOrDefault(kv.Key, 0);
                            var excess = kv.Value - needed;

                            if (excess > 0)
                            {
                                var toRemove = fulfillments
                                    .Join(vouchers.Where(v => v.Liters == kv.Key),
                                        f => f.VoucherId, v => v.Id,
                                        (f, v) => (Fulfillment: f, Voucher: v))
                                    .OrderByDescending(x => x.Fulfillment.FulfilledAtUtc)
                                    .Take(excess)
                                    .ToList();

                                excessVouchers.AddRange(toRemove);
                            }
                        }

                        foreach (var (fulfillment, voucher) in excessVouchers)
                        {
                            _context.Fulfillments.Remove(fulfillment);
                            voucher.Status = VoucherStatus.Available;
                            voucher.AssignedToUserId = null;
                            voucher.LegalEntityId = null;
                            voucher.WorkerUserId = null;
                            voucher.UpdatedAtUtc = DateTime.UtcNow;
                            _context.FuelVouchers.Update(voucher);

                            _logger.LogInformation(
                                "Removed fulfillment {FulfillmentId} for voucher {VoucherId} ({Liters}L) from order {OrderId}",
                                fulfillment.Id, voucher.Id, voucher.Liters, currentOrder.Id);
                        }

                        var remainingCount = fulfillments.Count - excessVouchers.Count;

                        currentOrder.Status = remainingCount > 0
                            ? OrderStatus.PartiallyFulfilled
                            : OrderStatus.PendingFulfillment;
                        currentOrder.FulfilledAtUtc = null;
                        currentOrder.PartiallyFulfilledSinceUtc = currentOrder.Status == OrderStatus.PartiallyFulfilled
                            ? DateTime.UtcNow
                            : null;
                        currentOrder.UpdatedAtUtc = DateTime.UtcNow;
                        _context.Orders.Update(currentOrder);

                        await _context.SaveChangesAsync(cancellationToken);

                        _logger.LogInformation(
                            "Order {OrderId} reset to {Status} after removing {Count} mismatched fulfillments",
                            currentOrder.Id, currentOrder.Status, excessVouchers.Count);
                    }

                    if (transaction != null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    if (currentOrder.Status == OrderStatus.PartiallyFulfilled)
                    {
                        await TryAutoRefundAsync(currentOrder.Id, cancellationToken);
                    }
                }
                catch
                {
                    if (transaction != null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                    throw;
                }
            }
        } while (hasMore);
    }

    private async Task ProcessOpenOrdersBackfillAsync(CancellationToken cancellationToken)
    {
        var openOrders = await _context.Orders
            .Include(o => o.LineItems)
            // Renewal orders are backfilled by ProcessOpenRenewalOrdersBackfillAsync, not here.
            // Exclude them so the buy-fuel path never assigns stock vouchers against a renewal
            // order's line items.
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                        && !_context.VoucherRenewalItems.Any(i => i.OrderId == o.Id))
            .OrderBy(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!openOrders.Any())
        {
            return;
        }

        _logger.LogInformation("Backfilling {Count} open orders", openOrders.Count);

        foreach (var order in openOrders)
        {
            try
            {
                await AssignVouchersToOrderAsync(order, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill order {OrderId}", order.Id);
            }
        }
    }

    public async Task ProcessVoucherImportsAsync(CancellationToken cancellationToken = default)
    {
        var unprocessedEvents = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.VoucherExpired)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!unprocessedEvents.Any())
        {
            return;
        }

        _logger.LogInformation("Processing {Count} VOUCHER_EXPIRED events", unprocessedEvents.Count);

        foreach (var outboxEvent in unprocessedEvents)
        {
            try
            {
                outboxEvent.Processed = true;
                outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                _context.OutboxEvents.Update(outboxEvent);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Processed VOUCHER_EXPIRED event {EventId}", outboxEvent.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process VOUCHER_EXPIRED event {EventId}", outboxEvent.Id);
            }
        }
    }

    private async Task ProcessOrderCreatedEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderCreatedPayload>(outboxEvent.Payload);

        if (payload?.OrderId == null)
        {
            _logger.LogWarning("Invalid ORDER_CREATED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var order = await _context.Orders
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == payload.OrderId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for event {EventId}", payload.OrderId, outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("Order {OrderId} already {Status}, marking event as processed", order.Id, order.Status);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        await AssignVouchersToOrderAsync(order, outboxEvent, cancellationToken);
    }

    private async Task AssignVouchersToOrderAsync(
        Order order,
        OutboxEvent? outboxEvent,
        CancellationToken cancellationToken)
    {
        // Each order in a batch is processed in isolation. Any entity force-attached while
        // handling a previous order (e.g. an Order via Update() in the partial path, or a
        // refund graph in the auto-refund handler) must never leak into the next order's
        // change tracker, otherwise a later SaveChanges can throw "Unexpected entry.EntityState:
        // Detached" (or an identity-conflict exception inside the refund handler).
        _context.ChangeTracker.Clear();

        var shouldAutoRefund = false;
        IDbContextTransaction? transaction = null;

        try
        {
            if (_context.Database.IsRelational())
            {
                // Serialize fulfillment per order across all processes (API replicas + JobsWorker)
                // and keep the voucher claim + fulfillment insert in ONE transaction so a failed
                // insert can never leave a voucher assigned without a fulfillment row.
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({order.Id.ToString()}))",
                    cancellationToken);

                var freshOrder = await _context.Orders
                    .AsNoTracking()
                    .Include(o => o.LineItems)
                    .FirstOrDefaultAsync(o => o.Id == order.Id, cancellationToken);

                if (freshOrder == null ||
                    freshOrder.Status == OrderStatus.Fulfilled ||
                    freshOrder.Status == OrderStatus.Cancelled)
                {
                    if (outboxEvent != null)
                    {
                        outboxEvent.Processed = true;
                        outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                        _context.OutboxEvents.Update(outboxEvent);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                order = freshOrder;
            }

            var lineItems = order.LineItems?.ToList() ?? [];
            var totalNeeded = lineItems.Sum(li => li.Quantity);

            if (totalNeeded == 0)
            {
                _logger.LogWarning("Order {OrderId} has no line items, skipping fulfillment", order.Id);
                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var alreadyAssignedCount = await _context.Fulfillments
                .CountAsync(f => f.OrderId == order.Id, cancellationToken);

            var vouchersNeeded = Math.Max(0, totalNeeded - alreadyAssignedCount);

            if (vouchersNeeded == 0)
            {
                var updatedCount = await TryMarkOrderFulfilledAsync(order.Id, cancellationToken);

                if (updatedCount > 0)
                {
                    _logger.LogInformation("Order {OrderId} already had all vouchers assigned, marked as fulfilled", order.Id);
                }
                else
                {
                    _logger.LogDebug("Order {OrderId} already fulfilled by another instance", order.Id);
                }

                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var vouchersAssigned = 0;
            var existingFulfillments = await _context.Fulfillments
                .Where(f => f.OrderId == order.Id)
                .ToListAsync(cancellationToken);

            var usedVoucherIds = existingFulfillments.Select(f => f.VoucherId).ToList();

            var existingFulfillmentVouchers = usedVoucherIds.Count != 0
                ? await _context.FuelVouchers
                    .Where(v => usedVoucherIds.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                : [];

            var assignedCounts = existingFulfillmentVouchers
                .GroupBy(v => new { Provider = v.Provider.ToLowerInvariant(), v.FuelTypeId, v.Liters })
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var lineItem in lineItems)
            {
                var key = new { Provider = lineItem.Provider.ToLowerInvariant(), lineItem.FuelTypeId, lineItem.Liters };
                var alreadyForThisLine = assignedCounts.GetValueOrDefault(key, 0);
                var remainingNeeded = Math.Max(0, lineItem.Quantity - alreadyForThisLine);

                for (int i = 0; i < remainingNeeded; i++)
                {
                    var availableVoucher = await FindMatchingVoucherAsync(
                        order, lineItem, usedVoucherIds, cancellationToken);

                    if (availableVoucher == null)
                    {
                        _logger.LogWarning(
                            "No available voucher for order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                            order.Id, lineItem.FuelTypeId, lineItem.Liters, i, lineItem.Quantity);

                        // The alert goes to staff Telegram, where a raw FuelTypeId GUID is useless.
                        // Resolve the display name (e.g. "ДП ЄВРО") and include provider + litres so
                        // staff know exactly which stock to top up; fall back to the id if unmatched.
                        var fuelTypeName = await _context.FuelTypes
                            .AsNoTracking()
                            .Where(ft => ft.Id == lineItem.FuelTypeId)
                            .Select(ft => ft.Name)
                            .FirstOrDefaultAsync(cancellationToken);

                        var fuelLabel = string.IsNullOrEmpty(fuelTypeName)
                            ? lineItem.FuelTypeId
                            : $"{fuelTypeName} ({lineItem.Provider.ToUpperInvariant()}, {lineItem.Liters:0.##} L)";

                        await _notifications.OrderUnfulfillableAsync(
                            order.Id, fuelLabel, i, lineItem.Quantity, cancellationToken);
                        break;
                    }

                    var assignedCount = await TryAssignVoucherAsync(availableVoucher.Id, order.UserId, order.LegalEntityId, order.Id, CustomerExpirationFor(lineItem), cancellationToken);

                    if (assignedCount == 0)
                    {
                        _logger.LogDebug("Voucher {VoucherId} already assigned by another instance, skipping", availableVoucher.Id);
                        continue;
                    }

                    var fulfillment = new Fulfillment
                    {
                        OrderId = order.Id,
                        VoucherId = availableVoucher.Id,
                        FulfilledAtUtc = DateTime.UtcNow
                    };

                    _context.Fulfillments.Add(fulfillment);
                    usedVoucherIds.Add(availableVoucher.Id);
                    vouchersAssigned++;

                    _logger.LogInformation(
                        "Assigned voucher {VoucherId} to order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                        availableVoucher.Id, order.Id, lineItem.FuelTypeId, lineItem.Liters, vouchersAssigned, vouchersNeeded);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var totalAssigned = alreadyAssignedCount + vouchersAssigned;

            if (totalAssigned >= totalNeeded)
            {
                var updatedCount = await TryMarkOrderFulfilledAsync(order.Id, cancellationToken);

                if (updatedCount > 0)
                {
                    _logger.LogInformation("Order {OrderId} fully fulfilled", order.Id);

                    // Match the orderId field with jsonb containment, NOT a substring LIKE.
                    // payload is a jsonb column and Postgres has no `jsonb ~~ jsonb` (LIKE)
                    // operator, so String.Contains here threw 42883 and - because this sits
                    // inside the per-order fulfillment transaction - rolled back the voucher
                    // claims and the Fulfilled status with it. `@>` also only matches the
                    // orderId field, where a substring could match the id inside any other.
                    var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });

                    var hasFulfilledEvent = await _context.OutboxEvents
                        .AnyAsync(e => e.EventType == OutboxEventType.OrderFulfilled
                                    && EF.Functions.JsonContains(e.Payload, orderProbe),
                                  cancellationToken);

                    if (!hasFulfilledEvent)
                    {
                        var fulfilledEvent = new OutboxEvent
                        {
                            EventType = OutboxEventType.OrderFulfilled,
                            Payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                orderId = order.Id,
                                userId = order.UserId,
                                fulfilledAt = DateTime.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = DateTime.UtcNow
                        };

                        _context.OutboxEvents.Add(fulfilledEvent);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    _logger.LogDebug("Order {OrderId} already marked as fulfilled by another instance", order.Id);
                }
            }
            else if (totalAssigned > 0)
            {
                var orderToUpdate = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == order.Id &&
                               (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled),
                               cancellationToken);

                if (orderToUpdate != null)
                {
                    orderToUpdate.Status = OrderStatus.PartiallyFulfilled;
                    orderToUpdate.FulfilledAtUtc = null;
                    // Keep the original timestamp when the order first became partially
                    // fulfilled so the auto-refund grace period is measured from then, not
                    // from every re-assignment run.
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= DateTime.UtcNow;
                    orderToUpdate.UpdatedAtUtc = DateTime.UtcNow;
                    _context.Orders.Update(orderToUpdate);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Order {OrderId} partially fulfilled: {Assigned}/{Needed} vouchers assigned",
                        order.Id, totalAssigned, totalNeeded);

                    shouldAutoRefund = true;
                }
            }

            if (outboxEvent != null)
            {
                outboxEvent.Processed = true;
                outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
                _context.OutboxEvents.Update(outboxEvent);
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }

        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(order.Id, cancellationToken);
        }
    }

    private async Task<FuelVoucher?> FindMatchingVoucherAsync(
        Order order,
        OrderLineItem lineItem,
        List<Guid> usedVoucherIds,
        CancellationToken cancellationToken)
    {
        // Expiry is enforced, not optional. With this filter commented out, the ascending
        // provider-term sort handed every paying customer the *most* expired stock first, and
        // nothing in the system ever flips stale rows to Expired (that is a manual admin
        // action), so the oldest unredeemable voucher was permanently at the head of the queue.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // TODO: temporary testing escape hatch — remove once the import workflow no longer
        // needs to run against expired test stock. Only ever flip it in a disposable/staging
        // environment; turning it off in production hands the most-expired stock to customers.
        var expiryEnabled = _configuration.GetValue<bool>("VoucherExpiration:Enabled", true);

        return await _context.FuelVouchers
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Provider.ToLower() == lineItem.Provider.ToLower()
                     && v.FuelTypeId == lineItem.FuelTypeId
                     && v.Liters == lineItem.Liters
                     && (!expiryEnabled || v.ProviderExpirationDate >= today)
                     && !usedVoucherIds.Contains(v.Id))
            .OrderBy(v => v.ProviderExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The customer-facing expiry for a line bought on a term: today plus that term. Null when the line
    /// carries no term, which means "sell the voucher's full remaining life" — the behaviour for every
    /// order placed before short terms existed.
    /// </summary>
    /// <remarks>
    /// A floor on the promise, not a gate on stock. A line asking for more life than the cheapest matching
    /// voucher has is still fulfilled; <see cref="TryAssignVoucherAsync"/> clamps the written date to the
    /// voucher's own provider term, so the customer gets exactly what the supplier backs and never more.
    /// Refusing the sale here instead would strand a paid order over a stock-mix detail.
    /// </remarks>
    private static DateOnly? CustomerExpirationFor(OrderLineItem lineItem)
        => VoucherRenewalTerms.TryFromCode(lineItem.TermCode, out var term)
            ? term.ApplyTo(DateOnly.FromDateTime(DateTime.UtcNow))
            : null;

    protected internal virtual async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "orders" SET status = 'Fulfilled', fulfilled_at_utc = {DateTime.UtcNow}, updated_at_utc = {DateTime.UtcNow}, partially_fulfilled_since_utc = NULL WHERE id = {orderId} AND (status = 'PendingFulfillment' OR status = 'PartiallyFulfilled')""",
            cancellationToken);

        return rowsAffected;
    }

    protected internal virtual async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, Guid orderId, DateOnly? customerExpiration, CancellationToken cancellationToken)
    {
        // The expiry predicate is repeated here on purpose: this UPDATE is the atomic claim, and
        // between the SELECT that chose this voucher and this statement the date can roll over
        // or an admin can edit the row. The claim itself must refuse expired stock.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
        // The provider term is the ceiling, so the promise is clamped rather than trusted: a line whose
        // term outruns the voucher gets the voucher's full remaining life, which is what it would have
        // got before short terms existed.
        var rowsAffected = customerExpiration is null
            ? await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND provider_expiration_date >= {expiryCutoff}""",
                cancellationToken)
            : await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, customer_expiration_date = LEAST({customerExpiration.Value}, provider_expiration_date), updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND provider_expiration_date >= {expiryCutoff}""",
                cancellationToken);

        return rowsAffected;
    }

    private async Task TryAutoRefundAsync(Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            // Auto-refund is a runtime setting, off by default. When enabled it only fires
            // after the order has been partially fulfilled for the configured grace period.
            if (!await _settings.IsAutoRefundEnabledAsync(cancellationToken))
            {
                return;
            }

            var sinceUtc = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.PartiallyFulfilledSinceUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (sinceUtc is null)
            {
                return;
            }

            var delayDays = await _settings.GetAutoRefundDelayDaysAsync(cancellationToken);
            if (DateTime.UtcNow - sinceUtc.Value < TimeSpan.FromDays(delayDays))
            {
                return;
            }

            // Don't hammer Monobank when a cancel keeps failing: retry a failed auto-refund
            // at most once an hour. (Manual refunds always retry immediately on demand.)
            var lastFailedAtUtc = await _context.Refunds
                .AsNoTracking()
                .Where(r => r.OrderId == orderId && r.Status == RefundStatus.Failed)
                .Select(r => (DateTime?)r.UpdatedAtUtc)
                .OrderByDescending(r => r)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastFailedAtUtc is not null &&
                DateTime.UtcNow - lastFailedAtUtc.Value < TimeSpan.FromMinutes(60))
            {
                return;
            }

            var result = await _refundHandler.HandleAsync(new RefundOrderCommand
            {
                OrderId = orderId,
                IsAutomatic = true
            }, cancellationToken);

            if (result.Status == "Processing")
            {
                _logger.LogInformation(
                    "Auto-refund {RefundId} initiated for partially fulfilled order {OrderId}: {Amount} kopecks",
                    result.RefundId, orderId, result.AmountKopecks);
            }
            else if (result.Status != "NothingToRefund" && result.Status != "NotPayable" && result.Status != "NotFound")
            {
                _logger.LogWarning(
                    "Auto-refund for order {OrderId} returned status {Status}: {Error}",
                    orderId, result.Status, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-refund failed for order {OrderId}", orderId);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Voucher renewal / replacement fulfilment (issue #80, slice 3).
    //
    // A renewal order carries voucher_renewal_items instead of the buy-fuel one-stock-per-unit
    // shape, so it is fulfilled by its own two-branch path here — never by AssignVouchersToOrderAsync
    // or the mismatch trimmer (both exclude renewal orders). Everything runs only AFTER payment:
    //   • Extend  — the source voucher is still valid: push its expiry out by the bought term
    //               (old expiry + term, leftover days kept), same row/code stays with the customer.
    //   • Replace — the source voucher has lapsed: claim a fresh stock voucher of the same
    //               provider/fuel/nominal valid at least until today+term, hand it to the customer,
    //               and flip the old one to Expired.
    // Idempotency: each item's FulfilledVoucherId marker is the done-flag, so a re-run (webhook +
    // per-minute backfill, or a retry) skips lines already applied. Concurrency mirrors buy-fuel:
    // one advisory xact lock per order id, one transaction around the whole order.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fulfils one paid renewal order: applies the extend/replace branch to each of its
    /// still-unfulfilled <see cref="VoucherRenewalItem"/>s, then marks the order Fulfilled (all
    /// applied), PartiallyFulfilled (some applied — auto-refund considers the rest) or leaves it
    /// PendingFulfillment (none applied — the backfill retries and a no-stock alert was raised).
    /// </summary>
    public async Task ProcessRenewalOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();

        var shouldAutoRefund = false;
        IDbContextTransaction? transaction = null;

        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext('fulfillment-order'), hashtext({orderId.ToString()}))",
                    cancellationToken);
            }

            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            // Only an open, paid renewal order is actionable. Terminal or unpaid states no-op.
            if (order == null ||
                (order.Status != OrderStatus.PendingFulfillment && order.Status != OrderStatus.PartiallyFulfilled))
            {
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var items = await _context.VoucherRenewalItems
                .AsTracking()
                .Where(i => i.OrderId == orderId)
                .ToListAsync(cancellationToken);

            if (items.Count == 0)
            {
                _logger.LogWarning("Renewal order {OrderId} has no renewal items, skipping", orderId);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return;
            }

            var sourceIds = items.Select(i => i.SourceVoucherId).Distinct().ToList();
            var sources = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => sourceIds.Contains(v.Id))
                .ToListAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;
            var usedStockIds = new List<Guid>();
            var fulfilledCount = 0;

            foreach (var item in items)
            {
                // Done-marker: this line was already applied by an earlier run.
                if (item.FulfilledVoucherId != null)
                {
                    fulfilledCount++;
                    continue;
                }

                var source = sources.FirstOrDefault(v => v.Id == item.SourceVoucherId);
                if (source == null)
                {
                    _logger.LogWarning(
                        "Renewal order {OrderId}: source voucher {VoucherId} not found, cannot fulfil line",
                        orderId, item.SourceVoucherId);
                    continue;
                }

                if (!VoucherRenewalTerms.TryFromCode(item.TermCode, out var term))
                {
                    _logger.LogWarning(
                        "Renewal order {OrderId}: unknown term code '{TermCode}' for voucher {VoucherId}",
                        orderId, item.TermCode, item.SourceVoucherId);
                    continue;
                }

                // Branch on what can actually be done to the source RIGHT NOW, using the same rule the
                // checkout used to price and sell this exact line. The date alone is not enough: a source
                // whose customer term still equals its supplier term has nothing to extend into, and
                // checkout already sold that as a Replace. Deciding on the date alone here would try to
                // extend, refuse past the ceiling, and leave the line unfulfilled with the customer
                // already paid - so the ceiling decides the branch, exactly as it did at sale time.
                var canExtendInPlace = source.CustomerExpirationDate >= today
                                       && VoucherRenewalEligibility.CanExtend(
                                           source.CustomerExpirationDate, source.ProviderExpirationDate, term);

                if (canExtendInPlace)
                {
                    var newExpiration = VoucherRenewalEligibility.NewExpirationForExtend(source.CustomerExpirationDate, term);

                    // Backstop, not the decision: the branch above already proved the term fits under the
                    // supplier term, so reaching here means the source row changed under us mid-transaction.
                    // Writing a customer date past the real term would promise validity the station may
                    // never honour, so we refuse rather than over-extend.
                    if (newExpiration > source.ProviderExpirationDate)
                    {
                        _logger.LogWarning(
                            "Renewal order {OrderId}: refusing to extend voucher {VoucherId} past its supplier term " +
                            "(customer {CustomerExpiration} + {TermCode} = {Requested}, provider {ProviderExpiration})",
                            orderId, source.Id, source.CustomerExpirationDate, item.TermCode, newExpiration,
                            source.ProviderExpirationDate);
                        continue;
                    }

                    var extended = await TryExtendVoucherAsync(
                        source.Id,
                        order.UserId,
                        source.CustomerExpirationDate,
                        source.ProviderExpirationDate,
                        newExpiration,
                        cancellationToken);
                    if (extended == 0)
                    {
                        _logger.LogWarning(
                            "Renewal order {OrderId}: could not extend voucher {VoucherId} (not Assigned to this user, already applied, or the row changed under us)",
                            orderId, source.Id);
                        continue;
                    }

                    item.FulfilledVoucherId = source.Id;
                    item.FulfilledAtUtc = now;
                    _context.Fulfillments.Add(new Fulfillment
                    {
                        OrderId = orderId,
                        VoucherId = source.Id,
                        FulfilledAtUtc = now
                    });
                    fulfilledCount++;

                    _logger.LogInformation(
                        "Renewal order {OrderId}: extended voucher {VoucherId} to {NewExpiration} (term {TermCode})",
                        orderId, source.Id, newExpiration, item.TermCode);
                }
                else
                {
                    var minExpiration = VoucherRenewalEligibility.MinStockExpirationForReplace(today, term);
                    var stock = await FindReplacementVoucherAsync(source, minExpiration, usedStockIds, cancellationToken);

                    if (stock == null)
                    {
                        var fuelTypeName = await _context.FuelTypes
                            .AsNoTracking()
                            .Where(ft => ft.Id == source.FuelTypeId)
                            .Select(ft => ft.Name)
                            .FirstOrDefaultAsync(cancellationToken);

                        var fuelLabel = string.IsNullOrEmpty(fuelTypeName)
                            ? source.FuelTypeId
                            : $"{fuelTypeName} ({source.Provider.ToUpperInvariant()}, {source.Liters:0.##} L)";

                        _logger.LogWarning(
                            "Renewal order {OrderId}: no replacement stock for {FuelLabel} valid until {MinExpiration}",
                            orderId, fuelLabel, minExpiration);

                        await _notifications.OrderUnfulfillableAsync(orderId, fuelLabel, 0, 1, cancellationToken);
                        continue;
                    }

                    // Inherit the SOURCE voucher's legal entity (null for a personal voucher, the
                    // company's id for a member's voucher) so a replaced company voucher stays with
                    // that company rather than silently becoming personal.
                    var claimed = await TryAssignReplacementVoucherAsync(
                        stock.Id, order.UserId, source.LegalEntityId, minExpiration, orderId, cancellationToken);

                    if (claimed == 0)
                    {
                        _logger.LogDebug(
                            "Renewal order {OrderId}: replacement voucher {VoucherId} claimed by another instance, skipping",
                            orderId, stock.Id);
                        continue;
                    }

                    // The old voucher is ours again: the customer now holds a fresh one, and this one is owed
                    // to the supplier for exchange. Clearing ownership is what surfaces it to the operator.
                    // Best-effort - 0 rows means it was already released or reassigned, an acceptable end
                    // state since the replacement is already in the customer's hands.
                    await TryReleaseReplacedVoucherAsync(source.Id, order.UserId, cancellationToken);

                    usedStockIds.Add(stock.Id);
                    item.FulfilledVoucherId = stock.Id;
                    item.FulfilledAtUtc = now;
                    _context.Fulfillments.Add(new Fulfillment
                    {
                        OrderId = orderId,
                        VoucherId = stock.Id,
                        FulfilledAtUtc = now
                    });
                    fulfilledCount++;

                    _logger.LogInformation(
                        "Renewal order {OrderId}: replaced expired voucher {SourceId} with stock {StockId} (term {TermCode})",
                        orderId, source.Id, stock.Id, item.TermCode);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (fulfilledCount >= items.Count)
            {
                var updated = await TryMarkOrderFulfilledAsync(orderId, cancellationToken);
                if (updated > 0)
                {
                    _logger.LogInformation("Renewal order {OrderId} fully fulfilled", orderId);

                    // jsonb containment dedup, matching the buy-fuel OrderFulfilled emit.
                    var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId });
                    var hasFulfilledEvent = await _context.OutboxEvents
                        .AnyAsync(e => e.EventType == OutboxEventType.OrderFulfilled
                                    && EF.Functions.JsonContains(e.Payload, orderProbe),
                                  cancellationToken);

                    if (!hasFulfilledEvent)
                    {
                        _context.OutboxEvents.Add(new OutboxEvent
                        {
                            EventType = OutboxEventType.OrderFulfilled,
                            Payload = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                orderId,
                                userId = order.UserId,
                                fulfilledAt = DateTime.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            else if (fulfilledCount > 0)
            {
                var orderToUpdate = await _context.Orders
                    .FirstOrDefaultAsync(o => o.Id == orderId &&
                               (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled),
                               cancellationToken);

                if (orderToUpdate != null)
                {
                    orderToUpdate.Status = OrderStatus.PartiallyFulfilled;
                    orderToUpdate.FulfilledAtUtc = null;
                    // Keep the first partial timestamp so the auto-refund grace is measured from
                    // then, not from every re-run.
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= DateTime.UtcNow;
                    orderToUpdate.UpdatedAtUtc = DateTime.UtcNow;
                    _context.Orders.Update(orderToUpdate);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Renewal order {OrderId} partially fulfilled: {Fulfilled}/{Total} items",
                        orderId, fulfilledCount, items.Count);

                    shouldAutoRefund = true;
                }
            }
            // fulfilledCount == 0: leave PendingFulfillment. The per-minute renewal backfill retries
            // and the no-stock alert has already been raised for each missing line.

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }

        // Same path as buy-fuel partials: refunds only the unfulfilled lines, gated by the
        // AutoRefund runtime setting and its grace window (this realises the "auto-refund that row"
        // decision for a post-payment no-stock replace).
        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(orderId, cancellationToken);
        }
    }

    /// <summary>
    /// Finds open renewal orders (at least one still-unfulfilled item) and re-runs their fulfilment.
    /// Folded into <see cref="ProcessPendingOrdersAsync"/> so the per-minute API job is the safety
    /// net when a webhook enqueue was lost or stock only arrived later.
    /// </summary>
    private async Task ProcessOpenRenewalOrdersBackfillAsync(CancellationToken cancellationToken)
    {
        var openRenewalOrderIds = await _context.Orders
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                     && _context.VoucherRenewalItems.Any(i => i.OrderId == o.Id && i.FulfilledVoucherId == null))
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
        DateOnly minExpiration,
        List<Guid> usedStockIds,
        CancellationToken cancellationToken)
    {
        // Same provider/fuel/nominal as the source, valid at least until today+term, not already
        // claimed in this run. Oldest-eligible first so longer-dated stock is preserved for tiers
        // that actually need it.
        return await _context.FuelVouchers
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Provider.ToLower() == source.Provider.ToLower()
                     && v.FuelTypeId == source.FuelTypeId
                     && v.Liters == source.Liters
                     && v.ProviderExpirationDate >= minExpiration
                     && !usedStockIds.Contains(v.Id))
            .OrderBy(v => v.ProviderExpirationDate)
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
            $"""UPDATE "fuel_vouchers" SET customer_expiration_date = {newExpiration}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned' AND customer_expiration_date = {expectedCustomerExpiration} AND {newExpiration} <= provider_expiration_date""",
            cancellationToken);
    }

    /// <summary>Atomic replacement claim: assign an Available voucher to the customer only if it is
    /// still valid at least until <paramref name="minExpiration"/> (today+term). The expiry gate is
    /// re-checked here, not just at select time, so an admin edit between SELECT and claim cannot
    /// hand out an under-term voucher. Returns rows affected (0 or 1).</summary>
    protected internal virtual async Task<int> TryAssignReplacementVoucherAsync(
        Guid voucherId, Guid userId, Guid? legalEntityId, DateOnly minExpiration, Guid orderId, CancellationToken cancellationToken)
    {
        // The renewal order owns the replacement, so order_id is written in the same atomic claim
        // as the assignment rather than inferred later from the Fulfillment row.
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, order_id = {orderId}, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available' AND provider_expiration_date >= {minExpiration}""",
            cancellationToken);
    }

    /// <summary>
    /// Releases the replaced customer's voucher back to the operator. The customer is done with it — we
    /// issued them a different voucher — so ownership is cleared, not just the status flipped. That is
    /// what puts the row back in the exchange attention list, which only surfaces stock (no assignee,
    /// no worker). From there the operator exchanges it with the supplier for a surcharge, and that
    /// surcharge is the voucher's final real cost.
    ///
    /// The chain stays walkable: this row's <c>Fulfillment</c> records the customer it used to belong to,
    /// <c>voucher_renewal_items.fulfilled_voucher_id</c> points at the voucher issued in its place, and the
    /// later <c>voucher_exchanges</c> row records what the supplier gave back and at what surcharge.
    ///
    /// Status is <c>Expired</c>, which is also why the loss job cannot double-book it: the expired-loss
    /// service only looks at Imported/VerifiedWithWarnings/Available, and the P&L excludes any voucher
    /// with a <c>Fulfillment</c> row from its expired bucket.
    /// </summary>
    protected internal virtual async Task<int> TryReleaseReplacedVoucherAsync(
        Guid voucherId, Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Expired', assigned_to_user_id = NULL, legal_entity_id = NULL, worker_user_id = NULL, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND assigned_to_user_id = {userId} AND status = 'Assigned'""",
            cancellationToken);
    }
}
