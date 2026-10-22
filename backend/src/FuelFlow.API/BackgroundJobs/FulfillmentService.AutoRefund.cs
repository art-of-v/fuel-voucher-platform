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
            if (SystemClock.UtcNow - sinceUtc.Value < TimeSpan.FromDays(delayDays))
            {
                return;
            }

            // Don't hammer Monobank when a cancel keeps failing: retry a failed auto-refund
            // at most once an hour. (Manual refunds always retry immediately on demand.)
var lastFailedAtUtc = await _context.Payments
    .AsNoTracking()
    .Where(p => p.OrderId == orderId && p.Kind == PaymentKind.Refund && p.Status == PaymentStatus.Failed)
    .Select(p => (DateTime?)p.UpdatedAtUtc)
    .OrderByDescending(r => r)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastFailedAtUtc is not null &&
                SystemClock.UtcNow - lastFailedAtUtc.Value < TimeSpan.FromMinutes(60))
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
}
