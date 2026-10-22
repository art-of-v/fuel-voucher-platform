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

    public async Task ProcessOrderCreatedEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderCreatedPayload>(outboxEvent.Payload);

        if (payload?.OrderId == null)
        {
            _logger.LogWarning("Invalid ORDER_CREATED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
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
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("Order {OrderId} already {Status}, marking event as processed", order.Id, order.Status);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
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

        // Staff alerts about stock that ran out are collected here and sent AFTER the transaction is
        // closed. They are Telegram HTTP calls, and this method holds a transaction across a long
        // stretch of work - holding one open across a network call is what the comment on the
        // repair pass warns about, and it is also why the alert used to be able to describe a
        // fulfilment that the transaction later rolled back.
        var pendingStockAlerts = new List<PendingStockAlert>();

        // The body of the transaction, as a local function: it reads the locals above, and the
        // helper needs it as a delegate it may run again.
        async Task ApplyAsync()
        {
            if (_context.Database.IsRelational())
            {
                // Serialize fulfillment per order across all processes (API replicas + JobsWorker)
                // and keep the voucher claim + fulfillment insert in ONE transaction so a failed
                // insert can never leave a voucher assigned without a fulfillment row.
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
                        outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
                        _context.OutboxEvents.Update(outboxEvent);
                        await _context.SaveChangesAsync(cancellationToken);
                    }

                    return;
                }

                order = freshOrder;
            }

            // The money question, asked before a single voucher is claimed. An order that owes
            // fuel has to have paid for it: the job selects its work by status alone, and the
            // status is a field a bug, an operator or a script can write. The payment row is the
            // money record - only a capture in state Succeeded means the money is ours - so this
            // is the check that holds even when nothing else does.
            //
            // The order is left alone rather than failed: the sweep picks it up again, and if the
            // money lands in the meantime it is fulfilled then. What must not happen is fuel for
            // nothing.
            if (!await OrderPayment.HasCapturedPaymentAsync(_context, order.Id, cancellationToken))
            {
                _logger.LogError(
                    "Order {OrderId} is eligible for fulfilment but has no captured payment; no vouchers were assigned",
                    order.Id);

                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            var lineItems = order.LineItems?.ToList() ?? [];
            var totalNeeded = lineItems.Sum(li => li.Quantity);

            if (totalNeeded == 0)
            {
                _logger.LogWarning("Order {OrderId} has no line items, skipping fulfillment", order.Id);
                if (outboxEvent != null)
                {
                    outboxEvent.Processed = true;
                    outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
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
                    outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
                    _context.OutboxEvents.Update(outboxEvent);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            var vouchersAssigned = 0;
            var existingFulfillments = await _context.Fulfillments
                .Where(f => f.OrderId == order.Id)
                .ToListAsync(cancellationToken);

            var usedVoucherIds = existingFulfillments.Select(f => f.VoucherId).ToList();

            // Read-only: this feeds a count, and the rows are raw-UPDATEd by the claim below.
            // AsNoTracking so an instance loaded before a claim never carries a stale xmin into a
            // later SaveChanges (#246).
            var existingFulfillmentVouchers = usedVoucherIds.Count != 0
                ? await _context.FuelVouchers
                    .AsNoTracking()
                    .Where(v => usedVoucherIds.Contains(v.Id))
                    .ToListAsync(cancellationToken)
                : [];

            var assignedCounts = existingFulfillmentVouchers
                .GroupBy(v => new { Brand = v.Brand.ToLowerInvariant(), v.FuelTypeId, v.Liters })
                .ToDictionary(g => g.Key, g => g.Count());

            // Which stock pool this order draws from: a QA test account is served only test vouchers,
            // every real account only real stock. Resolved once per order and re-asserted at the atomic
            // claim. IgnoreQueryFilters so this matches the raw-SQL claim predicate, which sees the row
            // regardless of any soft-delete filter.
            var isTestAccount = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == order.UserId)
                .Select(u => u.IsQaAccount)
                .FirstOrDefaultAsync(cancellationToken);

            foreach (var lineItem in lineItems)
            {
                var key = new { Brand = lineItem.Brand.ToLowerInvariant(), lineItem.FuelTypeId, lineItem.Liters };
                var alreadyForThisLine = assignedCounts.GetValueOrDefault(key, 0);
                var remainingNeeded = Math.Max(0, lineItem.Quantity - alreadyForThisLine);

                // The candidates of this line, read once. The units of one line take different
                // vouchers out of the same pool, in the order the pool hands them out - so the
                // line needs this many candidates, and reading them per unit asked the database
                // the same question once per unit.
                //
                // The list is read at the moment the line is reached, so a line that comes after
                // another one still sees the vouchers that one claimed.
                var candidates = remainingNeeded > 0
                    ? await FindMatchingVouchersAsync(
                        order, lineItem, usedVoucherIds, isTestAccount, remainingNeeded, cancellationToken)
                    : [];

                for (int i = 0; i < remainingNeeded; i++)
                {
                    var availableVoucher = candidates.Count > 0 ? candidates[0] : null;
                    if (availableVoucher is not null)
                    {
                        candidates.RemoveAt(0);
                    }

                    if (availableVoucher == null)
                    {
                        _logger.LogWarning(
                            "No available voucher for order {OrderId} line item {FuelType} {Liters}L ({Assigned}/{Needed})",
                            order.Id, lineItem.FuelTypeId, lineItem.Liters, i, lineItem.Quantity);

                        // The alert goes to staff Telegram, where a raw FuelTypeId GUID is useless.
                        // Resolve the display name (e.g. "ДП ЄВРО") and include brand + litres so
                        // staff know exactly which stock to top up; fall back to the id if unmatched.
                        var fuelTypeName = await _context.FuelTypes
                            .AsNoTracking()
                            .Where(ft => ft.Id == lineItem.FuelTypeId)
                            .Select(ft => ft.Name)
                            .FirstOrDefaultAsync(cancellationToken);

                        var fuelLabel = string.IsNullOrEmpty(fuelTypeName)
                            ? lineItem.FuelTypeId.ToString().ToString()
                            : $"{fuelTypeName} ({lineItem.Brand.ToUpperInvariant()}, {lineItem.Liters:0.##} L)";

                        // Collected, not sent: the alert goes out after the transaction is closed.
                        pendingStockAlerts.Add(new PendingStockAlert(order.Id, fuelLabel, i, lineItem.Quantity));
                        break;
                    }

                    var now = SystemClock.UtcNow;
                    var assignedCount = await TryAssignVoucherAsync(availableVoucher.Id, order.UserId, order.LegalEntityId, order.Id, CustomerExpirationFor(lineItem), cancellationToken);

                    if (assignedCount == 0)
                    {
                        _logger.LogDebug("Voucher {VoucherId} already assigned by another instance, skipping", availableVoucher.Id);
                        continue;
                    }

                    // This is the winning side of the CAS, which is the only reason the two records
                    // below describe something that happened rather than something we intended. Above the
                    // gate nothing moved, and a paper that stayed in the pool must not appear to have
                    // reached a customer.
                    //
                    // Two facts, two owners. The voucher is now in the customer's hands - a change of
                    // place, so a custody movement - and its status is no longer Available, which the
                    // movement deliberately does not say. Neither row derives the other, so writing
                    // only one loses half of the handover.
                    //
                    // `issued` is the plain buy. The renewal branch writes `replace_out` for the same
                    // physical fact (pool -> customer) because the reason differs, and the locations
                    // carry the physics; that split is why `issued` was sitting unwritten until now.
                    //
                    // amount_uah is deliberately NOT written, for the reason given in the renewal
                    // branch: the money is order_line_items.line_total, and this movement points at
                    // the same line through line_item_id, so a copy here is a second thing to keep in
                    // step for no reader. The column is for movements with no order behind them.
                    _context.VoucherMovements.Add(new VoucherMovement
                    {
                        Id = Guid.CreateVersion7(),
                        Kind = VoucherMovementKinds.Issued,
                        VoucherId = availableVoucher.Id,
                        FromLocation = VoucherLocations.Pool,
                        ToLocation = VoucherLocations.Customer,
                        OrderId = order.Id,
                        LineItemId = lineItem.Id,
                        ActorKind = VoucherActorKinds.User,
                        ActorUserId = order.UserId,
                        OccurredAtUtc = now
                    });

                    _context.VoucherEvents.Add(new VoucherEvent
                    {
                        Id = Guid.CreateVersion7(),
                        VoucherId = availableVoucher.Id,
                        EventType = VoucherEventTypes.StatusChanged,
                        // Taken from the CAS itself: it sets 'Assigned' WHERE status = 'Available'.
                        FromStatus = nameof(VoucherStatus.Available),
                        ToStatus = nameof(VoucherStatus.Assigned),
                        ExternalRef = order.Id.ToString(),
                        ActorKind = VoucherActorKinds.User,
                        ActorUserId = order.UserId,
                        OccurredAtUtc = now
                    });

                    var fulfillment = new Fulfillment
                    {
                        OrderId = order.Id,
                        VoucherId = availableVoucher.Id,
                        // The line is in scope here, so this is exact rather than inferred. Without it
                        // the reconciliation can only compare litres, which cannot tell two lines of
                        // the same volume apart - see Fulfillment.LineItemId.
                        LineItemId = lineItem.Id,
                        FulfilledAtUtc = SystemClock.UtcNow
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
                                fulfilledAt = SystemClock.UtcNow
                            }),
                            Processed = false,
                            CreatedAtUtc = SystemClock.UtcNow
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
                    orderToUpdate.PartiallyFulfilledSinceUtc ??= SystemClock.UtcNow;
                    orderToUpdate.UpdatedAtUtc = SystemClock.UtcNow;
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
                outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
                _context.OutboxEvents.Update(outboxEvent);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // it: a transient failure retries the body rather than failing the job, and the retry
        // claims the vouchers once.
        await _context.ExecuteReplaySafeAsync(_ => ApplyAsync(), cancellationToken);

        // The transaction is closed. The alerts go out now - a staff alert that says "this order
        // could not be filled" is worth sending, and worth sending about work that really happened.
        foreach (var alert in pendingStockAlerts)
        {
            await _notifications.OrderUnfulfillableAsync(
                alert.OrderId, alert.FuelLabel, alert.Assigned, alert.Needed, cancellationToken);
        }

        if (shouldAutoRefund)
        {
            await TryAutoRefundAsync(order.Id, cancellationToken);
        }
    }
}
