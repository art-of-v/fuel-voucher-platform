using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Recovers orders whose Monobank payment webhook was lost or arrived late.
///
/// The webhook (<see cref="ProcessMonobankWebhookCommandHandler"/>) is the primary path: when a
/// customer pays, Monobank pushes a callback and the order moves PendingPayment -> PendingFulfillment.
/// If that callback never arrives, the customer is charged but the order sits in PendingPayment
/// forever - no voucher, no fulfillment, no alert. This job is the safety net: it periodically asks
/// Monobank for the true status of aged awaiting-payment invoices and feeds the answer through the
/// same handler, so a poll-driven recovery is identical to a webhook-driven one (amount check,
/// stale/duplicate guards, state machine, ORDER_CREATED outbox, notifications and the fulfillment
/// enqueue all run once, in one place).
/// </summary>
public class MonobankReconciliationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly ProcessMonobankWebhookCommandHandler _webhookHandler;
    private readonly FuelFlowMetrics _metrics;
    private readonly MonobankOptions _options;
    private readonly ILogger<MonobankReconciliationService> _logger;

    public MonobankReconciliationService(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        ProcessMonobankWebhookCommandHandler webhookHandler,
        FuelFlowMetrics metrics,
        IOptions<MonobankOptions> options,
        ILogger<MonobankReconciliationService> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _webhookHandler = webhookHandler;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task ReconcilePendingPaymentsAsync(CancellationToken cancellationToken = default)
    {
        // Only run against the real Monobank client. When Monobank is disabled the container binds the
        // in-process mock, whose unknown-invoice fallback returns a fabricated "success" - polling that
        // would wrongly fulfill orders. In mock/dev the webhook path alone is enough.
        if (!_options.Enabled || !_options.ReconciliationEnabled)
        {
            _logger.LogDebug(
                "Monobank reconciliation skipped (Enabled={Enabled}, ReconciliationEnabled={Recon})",
                _options.Enabled, _options.ReconciliationEnabled);
            return;
        }

        var now = DateTime.UtcNow;
        var minAgeCutoff = now.AddMinutes(-_options.ReconciliationMinAgeMinutes);
        var maxAgeCutoff = now.AddHours(-_options.ReconciliationMaxAgeHours);

        // IgnoreQueryFilters: a customer can swipe-delete an unpaid checkout (soft delete) while its
        // invoice stays live and payable; if it is then paid and the webhook is lost, the order must
        // still be recoverable (the handler restores IsDeleted on a paid transition). Projection only -
        // never track the Order here, so the handler's own load+mutate cannot hit a tracking conflict.
        var candidates = await _context.Orders
            .IgnoreQueryFilters()
            .Where(o => o.Status == OrderStatus.PendingPayment
                     && o.MonobankInvoiceId != null
                     && o.CreatedAtUtc < minAgeCutoff
                     && o.CreatedAtUtc >= maxAgeCutoff)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_options.ReconciliationBatchSize)
            .Select(o => new { o.Id, InvoiceId = o.MonobankInvoiceId! })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogDebug("No aged awaiting-payment orders to reconcile");
            return;
        }

        _logger.LogInformation(
            "Reconciling {Count} aged awaiting-payment orders against Monobank", candidates.Count);

        foreach (var candidate in candidates)
        {
            try
            {
                var status = await _monobankClient.GetInvoiceStatusAsync(candidate.InvoiceId, cancellationToken);

                if (!IsTerminal(status.Status))
                {
                    // created / processing / hold: the customer has not finished paying. Leave the
                    // order awaiting payment and re-check on the next cycle.
                    _metrics.MonobankReconciliation("pending");
                    _logger.LogDebug("Order {OrderId} invoice {InvoiceId} still {Status} at Monobank",
                        candidate.Id, candidate.InvoiceId, status.Status);
                    continue;
                }

                // Feed the polled status through the webhook handler exactly as an inbound callback
                // would. Signature verification lives in the controller, not the handler, and is not
                // needed here: this status came from our own authenticated outbound call to Monobank.
                var command = new ProcessMonobankWebhookCommand
                {
                    InvoiceId = candidate.InvoiceId,
                    Status = status.Status,
                    Amount = status.Amount ?? 0,
                    CreatedDate = status.CreatedDate ?? DateTime.UtcNow,
                    ModifiedDate = status.ModifiedDate ?? DateTime.UtcNow,
                    CancelList = status.CancelList
                };

                var result = await _webhookHandler.HandleAsync(command, cancellationToken);

                var outcome = result.NewStatus switch
                {
                    nameof(OrderStatus.PendingFulfillment) => "recovered",
                    nameof(OrderStatus.Cancelled) => "cancelled",
                    _ => "noop"
                };
                _metrics.MonobankReconciliation(result.Success ? outcome : "rejected");

                if (result.Success
                    && result.NewStatus == nameof(OrderStatus.PendingFulfillment)
                    && result.PreviousStatus != nameof(OrderStatus.PendingFulfillment))
                {
                    // A recovered payment means a webhook was dropped. Log loud enough to alert on.
                    _logger.LogWarning(
                        "Reconciliation recovered order {OrderId} (invoice {InvoiceId}) that a lost or late webhook missed",
                        candidate.Id, candidate.InvoiceId);
                }
                else
                {
                    _logger.LogInformation(
                        "Reconciled order {OrderId} (invoice {InvoiceId}): Monobank {MonoStatus} -> {Result}",
                        candidate.Id, candidate.InvoiceId, status.Status, result.Message);
                }
            }
            catch (Exception ex)
            {
                // One invoice failing (a network blip, or a 404 on a purged invoice) must not stop the batch.
                _metrics.MonobankReconciliation("error");
                _logger.LogError(ex, "Failed to reconcile order {OrderId} (invoice {InvoiceId})",
                    candidate.Id, candidate.InvoiceId);
            }
        }
    }

    private static bool IsTerminal(string monobankStatus) =>
        monobankStatus.Equals("success", StringComparison.OrdinalIgnoreCase)
        || monobankStatus.Equals("failure", StringComparison.OrdinalIgnoreCase)
        || monobankStatus.Equals("reversed", StringComparison.OrdinalIgnoreCase)
        || monobankStatus.Equals("expired", StringComparison.OrdinalIgnoreCase);
}
