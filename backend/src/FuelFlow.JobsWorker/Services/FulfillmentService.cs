using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.JobsWorker.Models;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;

namespace FuelFlow.JobsWorker.Services;

public sealed class FulfillmentService : IFulfillmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(
        ApplicationDbContext context,
        ILogger<FulfillmentService> logger)
    {
        _context = context;
        _logger = logger;
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

        await ProcessOpenOrdersBackfillAsync(cancellationToken);
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
        var lineItems = order.LineItems?.ToList() ?? [];
        var totalNeeded = lineItems.Sum(li => li.Quantity);

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

            return;
        }

        var vouchersAssigned = 0;
        var usedVoucherIds = await _context.Fulfillments
            .Select(f => f.VoucherId)
            .ToListAsync(cancellationToken);

        foreach (var lineItem in lineItems)
        {
            for (int i = 0; i < lineItem.Quantity; i++)
            {
                var availableVoucher = await FindMatchingVoucherAsync(
                    order, lineItem, usedVoucherIds, cancellationToken);

                if (availableVoucher == null)
                {
                    _logger.LogWarning(
                        "No available voucher for order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                        order.Id, lineItem.FuelTypeId, lineItem.Liters, i, lineItem.Quantity);
                    break;
                }

                var assignedCount = await TryAssignVoucherAsync(availableVoucher.Id, order.UserId, cancellationToken);

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
                     && !usedVoucherIds.Contains(v.Id))
            .OrderBy(v => v.ExpirationDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId &&
                       (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled),
                       cancellationToken);

        if (order == null)
            return 0;

        order.Status = OrderStatus.Fulfilled;
        order.FulfilledAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, CancellationToken cancellationToken)
    {
        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(v => v.Id == voucherId && v.Status == VoucherStatus.Available,
                       cancellationToken);

        if (voucher == null)
            return 0;

        voucher.Status = VoucherStatus.Assigned;
        voucher.AssignedToUserId = userId;
        voucher.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return 1;
    }
}
