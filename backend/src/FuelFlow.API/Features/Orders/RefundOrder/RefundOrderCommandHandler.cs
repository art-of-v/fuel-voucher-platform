using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.API.Features.Orders.RefundOrder;

public sealed class RefundOrderCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClientFactory _monobankClientFactory;
    private readonly ProviderEventService _providerEventService;

    public RefundOrderCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        ProviderEventService providerEventService)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _providerEventService = providerEventService;
    }

    public async Task<RefundOrderResult> HandleAsync(
        RefundOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);

        if (order is null)
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NotFound",
                ErrorMessage = "Order not found"
            };
        }

        if (string.IsNullOrEmpty(order.MonobankInvoiceId))
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NotPayable",
                ErrorMessage = "Order has no Monobank invoice to refund"
            };
        }

        // AsTracking: the app-wide query default is NoTracking, but the retry and
        // amount-correction branches below mutate this entity and rely on SaveChanges.
        var existing = await _context.Refunds
            .AsTracking()
            .FirstOrDefaultAsync(r => r.OrderId == command.OrderId, cancellationToken);

        // A refund already in flight (Processing) or confirmed (Completed) is reported back
        // as-is and must never be cancelled twice. Only a Failed refund is retryable.
        if (existing is not null && existing.Status != RefundStatus.Failed)
        {
            var correctedAmount = ComputeRefundAmountKopecks(order);

            if (correctedAmount > 0 && existing.Amount != correctedAmount)
            {
                existing.Amount = correctedAmount;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Only a refund confirmed by Monobank (Completed) may move the order into a
            // refunded state. While the refund is still Processing the order keeps its
            // fulfillment-derived status, otherwise the admin sees the contradictory
            // "PartiallyRefunded + Refund pending" combination.
            if (existing.Status == RefundStatus.Completed)
            {
                var deliveredCount = await _context.Fulfillments
                    .CountAsync(f => f.OrderId == order.Id, cancellationToken);

                var targetStatus = deliveredCount > 0
                    ? OrderStatus.PartiallyRefunded
                    : OrderStatus.Refunded;

                if (order.Status != targetStatus)
                {
                    ApplyOrderStatus(order, targetStatus);
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            return new RefundOrderResult
            {
                RefundId = existing.Id,
                OrderId = existing.OrderId,
                AmountKopecks = existing.Amount,
                Status = existing.Status.ToString(),
                ErrorMessage = existing.ErrorMessage
            };
        }

        var refundableAmount = ComputeRefundAmountKopecks(order);

        // Safety cap: even when the caller (admin UI or the automatic
        // partial-fulfillment job) supplies an amount, never refund more than
        // the unfulfilled value left on the order — an over-refund would hand
        // back money for vouchers that were already delivered.
        var amount = Math.Min(command.AmountKopecks ?? refundableAmount, refundableAmount);

        if (amount <= 0)
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NothingToRefund",
                // Name the reason. "No unfulfilled value remains" is the correct answer for a
                // fully-fulfilled order and a misleading one for an order nobody ever paid for -
                // and the admin UI surfaces this string verbatim, so a vague message here is
                // what turns a predictable refusal into a support question.
                ErrorMessage = order.MonobankStatus != MonobankStatus.Success
                    ? $"Order was not paid (provider status: {order.MonobankStatus?.ToString() ?? "unknown"}); there is nothing to return"
                    : "No unfulfilled value remains on this order"
            };
        }

        var extRef = order.IdempotencyKey ?? order.Id.ToString();

        // A previously Failed refund is retried in place instead of creating a second row.
        Refund refund;
        if (existing is not null)
        {
            refund = existing;
            refund.Amount = amount;
            refund.Status = RefundStatus.Processing;
            refund.ErrorMessage = null;
            refund.MonobankStatus = null;
            refund.IsAutomatic = command.IsAutomatic;
            if (command.ChangedByUserId is not null)
            {
                refund.CreatedByUserId = command.ChangedByUserId;
            }
            if (!string.IsNullOrWhiteSpace(command.ChangedByUserName))
            {
                refund.CreatedByUserName = command.ChangedByUserName;
            }
            refund.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            refund = new Refund
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                UserId = order.UserId,
                Amount = amount,
                InvoiceId = order.MonobankInvoiceId,
                ExtRef = extRef,
                Status = RefundStatus.Processing,
                CreatedByUserId = command.ChangedByUserId,
                CreatedByUserName = command.ChangedByUserName,
                IsAutomatic = command.IsAutomatic,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.Refunds.Add(refund);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // A concurrent refund request (e.g. admin double-click) won the race past the
            // existence check; the unique index on OrderId rejects the second insert.
            // Report the winning refund instead of surfacing a 500.
            _context.ChangeTracker.Clear();

            var winner = await _context.Refunds
                .FirstOrDefaultAsync(r => r.OrderId == order.Id, cancellationToken);

            if (winner is null)
            {
                throw;
            }

            return new RefundOrderResult
            {
                RefundId = winner.Id,
                OrderId = winner.OrderId,
                AmountKopecks = winner.Amount,
                Status = winner.Status.ToString(),
                ErrorMessage = winner.ErrorMessage
            };
        }

        var changedByUserId = command.ChangedByUserId ?? Guid.Empty;
        var changedByUserName = command.ChangedByUserName ?? (command.IsAutomatic ? "System" : null);

        try
        {
            var merchant = order.MonobankMerchant ?? MonobankMerchant.Live;
            var monobankClient = _monobankClientFactory.ForMerchant(merchant);

            var response = await monobankClient.CancelInvoiceAsync(
                order.MonobankInvoiceId,
                amount,
                extRef,
                cancellationToken);

            // Do NOT update order status here - wait for Monobank confirmation via RefundStatusSyncService
            // Order status update is delayed to avoid confusing "PartiallyRefunded + Refund pending" state
            // Order status will be updated by RefundStatusSyncService when Monobank confirms

            refund.MonobankStatus = response.Status;
            refund.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refund.Id.ToString(),
                eventType: "RefundRequested",
                oldValue: null,
                newValue: JsonSerializer.Serialize($"{amount} kopecks (order {order.Id})"),
                changedByUserId: changedByUserId,
                changedByUserName: changedByUserName,
                summary: command.IsAutomatic
                    ? $"Automatic refund of {amount} kopecks for partially fulfilled order"
                    : $"Manual refund of {amount} kopecks requested by admin",
                providerId: order.LineItems.FirstOrDefault()?.Provider ?? "unknown",
                ct: cancellationToken);

            return new RefundOrderResult
            {
                RefundId = refund.Id,
                OrderId = order.Id,
                AmountKopecks = amount,
                Status = "Processing",
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            refund.Status = RefundStatus.Failed;
            // error_message column is limited to 500 chars; a longer provider exception
            // (e.g. a full HTML error body) would make this SaveChanges throw and turn a
            // handled refund failure into an unhandled 500.
            refund.ErrorMessage = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            refund.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refund.Id.ToString(),
                eventType: "RefundFailed",
                oldValue: null,
                newValue: JsonSerializer.Serialize(ex.Message),
                changedByUserId: changedByUserId,
                changedByUserName: changedByUserName,
                summary: $"Refund of {amount} kopecks for order {order.Id} failed: {ex.Message}",
                providerId: order.LineItems.FirstOrDefault()?.Provider ?? "unknown",
                ct: cancellationToken);

            return new RefundOrderResult
            {
                RefundId = refund.Id,
                OrderId = order.Id,
                AmountKopecks = amount,
                Status = "Failed",
                ErrorMessage = ex.Message
            };
        }
    }

    private void ApplyOrderStatus(Order order, OrderStatus status)
    {
        var trackedOrder = _context.ChangeTracker.Entries<Order>()
            .FirstOrDefault(e => e.Entity.Id == order.Id)?.Entity;

        if (trackedOrder is not null)
        {
            trackedOrder.Status = status;
            trackedOrder.UpdatedAtUtc = DateTime.UtcNow;
            if (status != OrderStatus.PartiallyFulfilled)
            {
                trackedOrder.PartiallyFulfilledSinceUtc = null;
            }
            return;
        }

        order.Status = status;
        order.UpdatedAtUtc = DateTime.UtcNow;
        if (status != OrderStatus.PartiallyFulfilled)
        {
            order.PartiallyFulfilledSinceUtc = null;
        }
        _context.Orders.Update(order);
    }

    /// <summary>Total ordered value in kopecks (all line items, regardless of fulfillment).</summary>
    internal static int ComputeTotalValueKopecks(Order order) =>
        (int)Math.Round(
            Money.ToKopecks(order.LineItems.Sum(li => li.UnitPrice * li.Quantity)),
            MidpointRounding.AwayFromZero);

    /// <summary>Value of delivered vouchers in kopecks (total ordered value minus unfulfilled value).</summary>
    /// <remarks>
    /// The unpaid case cannot be derived from the subtraction below. Once an unpayable order
    /// reports nothing refundable, `total - 0` would read as "delivered in full" and count an
    /// abandoned checkout as fulfilled revenue in both GetReport and GetReconciliation. Nothing
    /// was delivered, so the answer is zero.
    /// </remarks>
    internal static int ComputeFulfilledValueKopecks(Order order) =>
        !WasPaid(order) ? 0 : ComputeTotalValueKopecks(order) - ComputeRefundAmountKopecks(order);

    /// <summary>
    /// Whether the provider confirmed this order was paid.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the provider's status rather than the order status: an order can be
    /// Cancelled and still have been paid (the invoice stays payable for its whole validity
    /// window), and that is exactly the case where value must still be accounted for.
    /// </remarks>
    private static bool WasPaid(Order order) => order.MonobankStatus == MonobankStatus.Success;

    internal static int ComputeRefundAmountKopecks(Order order)
    {
        // A company handover never captured money (Price is 0), so there is nothing to give back.
        // The write path is already blocked upstream — such an order carries no Monobank invoice —
        // so this only keeps a displayed amount from implying otherwise.
        if (order.Kind == OrderKind.ReceivedFromCompany) return 0;

        // Nothing to return unless the provider says it took the money.
        //
        // Without this, every order that exists but was never paid reports its FULL price as
        // refundable: an unpaid checkout, and an order whose invoice creation failed. The admin
        // button is gated purely on this number (admin.tsx:2629), so both render as a live action
        // and both fail on the provider's terms instead of ours — an operator clicks "refund" on
        // an order nobody paid for and gets a Monobank error back.
        //
        // Keyed on the provider's status rather than the order status on purpose. An order can
        // be Cancelled and still be owed a refund: the invoice stays payable for its whole
        // validity window, so a customer can pay one long after a failure cancelled it. Gating on
        // OrderStatus would have removed the only remedy for exactly that case.
        if (!WasPaid(order)) return 0;

        var grouped = order.LineItems
            .GroupBy(li => (li.Provider, li.FuelTypeId, li.Liters))
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.FuelTypeId,
                g.Key.Liters,
                OrderedUnits = g.Sum(li => li.Quantity),
                UnitPrice = g.Max(li => li.UnitPrice)
            })
            .ToList();

        var unfulfilledValue = 0m;

        foreach (var group in grouped)
        {
            var fulfilledUnits = order.Fulfillments
                .Count(f => f.Voucher is not null
                    && string.Equals(f.Voucher!.Provider, group.Provider, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == group.FuelTypeId
                    && f.Voucher.Liters == group.Liters);

            var unfulfilledUnits = group.OrderedUnits - fulfilledUnits;
            if (unfulfilledUnits > 0)
            {
                unfulfilledValue += unfulfilledUnits * group.UnitPrice;
            }
        }

        return (int)Math.Round(Money.ToKopecks(unfulfilledValue), MidpointRounding.AwayFromZero);
    }
}
