using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Permanently (hard-)deletes abandoned orders that have outlived their usefulness.
///
/// A customer can swipe-delete an unpaid checkout: that is a *soft* delete (<c>Order.IsDeleted=true</c>)
/// and the row lingers. <see cref="MonobankReconciliationService"/> later drives the abandoned invoice
/// to a terminal state, so an unpaid + soft-deleted order settles as <c>IsDeleted=true</c> +
/// <c>Cancelled</c>. Nothing else ever removes those rows, so they accumulate indefinitely and clutter
/// the admin purchases view (which reads with <c>IgnoreQueryFilters</c>). This job garbage-collects them.
///
/// The purge predicate — <c>IsDeleted</c> AND <c>Status == Cancelled</c> AND aged past the retention
/// window — is deliberately conservative and self-excludes any paid order: a payment (even one arriving
/// after the soft delete) flips <c>Status</c> off <c>PendingPayment</c>/<c>Cancelled</c> AND restores
/// <c>IsDeleted=false</c> in the webhook handler, so a recoverable order is never eligible. Deletion
/// cascades to <c>order_line_items</c> (FK ON DELETE CASCADE). Runtime-gated: does nothing unless an
/// admin has turned it on, because a hard delete cannot be undone.
/// </summary>
public class OrderCleanupService
{
    // Bounds a single run to one cheap transaction; a larger backlog drains over successive daily
    // runs rather than deleting an unbounded number of rows (and their cascades) in one shot.
    private const int BatchSize = 500;

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<OrderCleanupService> _logger;

    public OrderCleanupService(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        ILogger<OrderCleanupService> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public virtual async Task CleanupAbandonedOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (!await _settings.IsOrderCleanupEnabledAsync(cancellationToken))
        {
            _logger.LogDebug("Order cleanup skipped (OrderCleanup:Enabled is off)");
            return;
        }

        var retentionDays = Math.Max(1, await _settings.GetOrderCleanupRetentionDaysAsync(cancellationToken));
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // AsTracking + Include so EF itself cascades the delete to the loaded line items (robust
        // regardless of the provider's DB-level FK behaviour); IgnoreQueryFilters because every
        // candidate is, by definition, soft-deleted and hidden by the global !IsDeleted filter.
        //
        // !FuelVouchers.Any is not an optimisation, it is required: fuel_vouchers.order_id is ON
        // DELETE RESTRICT, so an order that still owns a voucher cannot be removed at all. One such
        // row would abort the whole batch (and be re-selected every night, so the job would never
        // drain again). An order that delivered fuel is never really abandoned - leave it.
        var abandoned = await _context.Orders
            .IgnoreQueryFilters()
            .AsTracking()
            .Include(o => o.LineItems)
            .Where(o => o.IsDeleted
                     && o.Status == OrderStatus.Cancelled
                     && o.UpdatedAtUtc < cutoff)
            .Where(o => !_context.FuelVouchers.IgnoreQueryFilters().Any(v => v.OrderId == o.Id))
            .OrderBy(o => o.UpdatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            _logger.LogDebug("No abandoned orders to purge (retention {RetentionDays}d)", retentionDays);
            return;
        }

        _context.Orders.RemoveRange(abandoned);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Purged {Count} abandoned order(s) soft-deleted and cancelled before {Cutoff:u} (retention {RetentionDays}d)",
            abandoned.Count, cutoff, retentionDays);
    }
}
