using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.JobsWorker.Models;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FuelFlow.JobsWorker.Services;

public class FulfillmentService : IFulfillmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FulfillmentService> _logger;
    private readonly FuelFlowMetrics _metrics;
    private readonly NotificationDispatcher _notifications;

    public FulfillmentService(
        ApplicationDbContext context,
        ILogger<FulfillmentService> logger,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications)
    {
        _context = context;
        _logger = logger;
        _metrics = metrics;
        _notifications = notifications;
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
                    _metrics.FulfillmentFailed(ex.GetType().Name);
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
                .Where(o => o.Status == OrderStatus.Fulfilled)
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
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
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
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("Order {OrderId} already {Status}, marking event as processed", order.Id, order.Status);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
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
        IDbContextTransaction? transaction = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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

                        await _notifications.OrderUnfulfillableAsync(
                            order.Id, lineItem.FuelTypeId, i, lineItem.Quantity, cancellationToken);
                        break;
                    }

                    var assignedCount = await TryAssignVoucherAsync(availableVoucher.Id, order.UserId, order.LegalEntityId, cancellationToken);

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

                    _metrics.VoucherAssigned(
                        availableVoucher.Provider,
                        availableVoucher.FuelTypeId,
                        order.LegalEntityId.HasValue);

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
                    _metrics.FulfillmentSucceeded(stopwatch.Elapsed.TotalMilliseconds);
                    _logger.LogInformation("Order {OrderId} fully fulfilled", order.Id);

                    var orderIdString = order.Id.ToString();
                    var fulfilledEvents = await _context.OutboxEvents
                        .Where(e => e.EventType == OutboxEventType.OrderFulfilled)
                        .Select(e => e.Payload)
                        .ToListAsync(cancellationToken);

                    var hasFulfilledEvent = fulfilledEvents.Any(payload => payload.Contains(orderIdString));

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
                    orderToUpdate.UpdatedAtUtc = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Order {OrderId} partially fulfilled: {Assigned}/{Needed} vouchers assigned",
                        order.Id, totalAssigned, totalNeeded);
                }
            }

            if (outboxEvent != null)
            {
                outboxEvent.Processed = true;
                outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
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
    }

    private async Task<FuelVoucher?> FindMatchingVoucherAsync(
        Order order,
        OrderLineItem lineItem,
        List<Guid> usedVoucherIds,
        CancellationToken cancellationToken)
    {
        return await _context.FuelVouchers
            .Where(v => v.Status == VoucherStatus.Available
                     && v.Provider.ToLower() == lineItem.Provider.ToLower()
                     && v.FuelTypeId == lineItem.FuelTypeId
                     && v.Liters == lineItem.Liters
                     // TODO: uncomment to exclude expired vouchers
                     // && v.ExpirationDate >= DateOnly.FromDateTime(DateTime.UtcNow)
                     && !usedVoucherIds.Contains(v.Id))
            .OrderBy(v => v.ExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected internal virtual async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "orders" SET status = 'Fulfilled', fulfilled_at_utc = {DateTime.UtcNow}, updated_at_utc = {DateTime.UtcNow} WHERE id = {orderId} AND (status = 'PendingFulfillment' OR status = 'PartiallyFulfilled')""",
            cancellationToken);

        return rowsAffected;
    }

    protected internal virtual async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, CancellationToken cancellationToken)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "fuel_vouchers" SET status = 'Assigned', assigned_to_user_id = {userId}, legal_entity_id = {legalEntityId}, worker_user_id = NULL, updated_at_utc = {DateTime.UtcNow} WHERE id = {voucherId} AND status = 'Available'""",
            cancellationToken);

        return rowsAffected;
    }
}
