using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.Features.Orders.RefundOrder;

public sealed class RefundOrderCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly ProviderEventService _providerEventService;

    public RefundOrderCommandHandler(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        ProviderEventService providerEventService)
    {
        _context = context;
        _monobankClient = monobankClient;
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

        var existing = await _context.Refunds
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == command.OrderId, cancellationToken);

        if (existing is not null)
        {
            return new RefundOrderResult
            {
                RefundId = existing.Id,
                OrderId = existing.OrderId,
                AmountKopecks = existing.Amount,
                Status = existing.Status.ToString(),
                ErrorMessage = existing.ErrorMessage
            };
        }

        var amount = command.AmountKopecks ?? ComputeRefundAmountKopecks(order);

        if (amount <= 0)
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NothingToRefund",
                ErrorMessage = "No unfulfilled value remains on this order"
            };
        }

        var extRef = order.IdempotencyKey ?? order.Id.ToString();
        var refund = new Refund
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
        await _context.SaveChangesAsync(cancellationToken);

        var changedByUserId = command.ChangedByUserId ?? Guid.Empty;
        var changedByUserName = command.ChangedByUserName ?? (command.IsAutomatic ? "System" : null);

        try
        {
            var response = await _monobankClient.CancelInvoiceAsync(
                order.MonobankInvoiceId,
                amount,
                extRef,
                cancellationToken);

            refund.MonobankStatus = response.Status;
            refund.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            var deliveredCount = await _context.Fulfillments
                .CountAsync(f => f.OrderId == order.Id, cancellationToken);

            var newStatus = deliveredCount > 0
                ? OrderStatus.PartiallyRefunded
                : OrderStatus.Refunded;

            order.Status = newStatus;
            order.UpdatedAtUtc = DateTime.UtcNow;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refund.Id.ToString(),
                eventType: "RefundRequested",
                oldValue: null,
                newValue: $"{amount} kopecks (order {order.Id})",
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
            refund.ErrorMessage = ex.Message;
            refund.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refund.Id.ToString(),
                eventType: "RefundFailed",
                oldValue: null,
                newValue: ex.Message,
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

    internal static int ComputeRefundAmountKopecks(Order order)
    {
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
                    && f.Voucher!.Provider == group.Provider
                    && f.Voucher.FuelTypeId == group.FuelTypeId
                    && f.Voucher.Liters == group.Liters);

            var unfulfilledUnits = group.OrderedUnits - fulfilledUnits;
            if (unfulfilledUnits > 0)
            {
                unfulfilledValue += unfulfilledUnits * group.UnitPrice;
            }
        }

        return (int)Math.Round(unfulfilledValue * 100, MidpointRounding.AwayFromZero);
    }
}
