using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Time;
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
public class DeletedUnpaidOrderCleanupService
{
    // Bounds a single run to one cheap transaction; a larger backlog drains over successive daily
    // runs rather than deleting an unbounded number of rows (and their cascades) in one shot.
    private const int BatchSize = 500;

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<DeletedUnpaidOrderCleanupService> _logger;

    public DeletedUnpaidOrderCleanupService(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        ILogger<DeletedUnpaidOrderCleanupService> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public virtual async Task CleanupAbandonedOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (!await _settings.IsDeletedUnpaidOrderCleanupEnabledAsync(cancellationToken))
        {
            _logger.LogDebug("Order cleanup skipped (DeletedUnpaidOrderCleanup:Enabled is off)");
            return;
        }

        var retentionDays = Math.Max(1, await _settings.GetDeletedUnpaidOrderCleanupRetentionDaysAsync(cancellationToken));
        var cutoff = SystemClock.UtcNow.AddDays(-retentionDays);

        // AsTracking + Include so EF itself cascades the delete to the loaded line items (robust
        // regardless of the brand's DB-level FK behaviour); IgnoreQueryFilters because every
        // candidate is, by definition, soft-deleted and hidden by the global !IsDeleted filter.
        //
        // !FuelVouchers.Any is not an optimisation, it is required: fuel_vouchers.order_id is ON
        // DELETE RESTRICT, so an order that still owns a voucher cannot be removed at all. One such
        // row would abort the whole batch (and be re-selected every night, so the job would never
        // drain again). An order that delivered fuel is never really abandoned - leave it.
        //
        // The refunds guard exists for the same reason, and was added alongside making that FK RESTRICT. A
        // refunded order is not an abandoned checkout: money went out, and "we paid this back, for
        // this, under this order" is answerable only from the refund row. The status predicate already
        // excludes them today - a refund flips the order to PartiallyRefunded/Refunded, never Cancelled -
        // so this clause is not load-bearing today. It is there so that if that ever stops being true,
        // the job skips the order instead of the database refusing the delete and wedging the cleanup.
        //
        // There is deliberately no renewal guard any more. It used to be "this order has renewal items",
        // which was the job asking the database whether it could delete the row. The database now says
        // no on its own - order_line_items.order_id is RESTRICT - so the guard was removed and the
        // explicit line removal below is what this job adapts to instead.
        var abandoned = await _context.Orders
            .IgnoreQueryFilters()
            .AsTracking()
            .Include(o => o.LineItems)
            .Where(o => o.IsDeleted
                     && o.Status == OrderStatus.Cancelled
                     && o.UpdatedAtUtc < cutoff)
            .Where(o => !_context.FuelVouchers.IgnoreQueryFilters().Any(v => v.OrderId == o.Id))
            .Where(o => !_context.Payments.IgnoreQueryFilters()
                .Any(p => p.OrderId == o.Id && p.Kind == PaymentKind.Refund))
            .OrderBy(o => o.UpdatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            _logger.LogDebug("No abandoned orders to purge (retention {RetentionDays}d)", retentionDays);
            return;
        }

        // The lines go first, and explicitly. `order_line_items.order_id` is RESTRICT - the renewal line is
        // what now keeps an order a customer paid a renewal on from being deleted, so removing the order
        // while its lines exist is refused by EF before the database is even asked. They are loaded by
        // the Include above, which is what makes this a tracked removal rather than a second query.
        //
        // This is a job whose entire purpose is deleting abandoned checkouts, so it has to be the
        // caller that adapts: the alternative would be weakening the constraint back to CASCADE, which
        // is the direction this whole table was moved away from.
        foreach (var order in abandoned)
        {
            _context.OrderLineItems.RemoveRange(order.LineItems);
        }

        _context.Orders.RemoveRange(abandoned);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Purged {Count} abandoned order(s) soft-deleted and cancelled before {Cutoff:u} (retention {RetentionDays}d)",
            abandoned.Count, cutoff, retentionDays);
    }
}
