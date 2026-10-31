using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using Hangfire;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed partial class ProcessMonobankWebhookCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessMonobankWebhookCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly RefundStatusSyncService _refundStatusSyncService;
    private readonly FuelFlowMetrics _metrics;
    private readonly NotificationDispatcher _notifications;
    private readonly JournalPostingService _journal;

    public ProcessMonobankWebhookCommandHandler(
        ApplicationDbContext context,
        ILogger<ProcessMonobankWebhookCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient,
        RefundStatusSyncService refundStatusSyncService,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications,
        JournalPostingService journal)
    {
        _context = context;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
        _refundStatusSyncService = refundStatusSyncService;
        _metrics = metrics;
        _notifications = notifications;
        _journal = journal;
    }

    /// <summary>
    /// What the processing step decided: the answer the acquirer gets, and what still has to
    /// happen once the money step is committed.
    /// </summary>
    /// <param name="Response">What the webhook answers the acquirer.</param>
    /// <param name="Order">The order the delivery concerned, when there was one.</param>
    /// <param name="TransitionedTo">
    /// The status the order was moved to, or null when no transition happened. Null is what keeps a
    /// replayed or a refused delivery from announcing a payment and from queueing a fulfilment.
    /// </param>
    private sealed record ProcessingOutcome(
        ProcessMonobankWebhookResponse Response,
        Order? Order,
        OrderStatus? TransitionedTo);

    public async Task<ProcessMonobankWebhookResponse> HandleAsync(
        ProcessMonobankWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        _metrics.MonobankWebhookReceived(command.Status.ToLowerInvariant());

        _logger.LogInformation(
            "Processing Monobank webhook for invoice {InvoiceId}, status: {Status}",
            command.InvoiceId,
            command.Status);

        // Stored before anything acts on it. Until this existed the delivery existed only for the
        // duration of the request: the body was read in the controller, handed over, and never
        // written anywhere. A customer disputing a charge had nothing to show.
        //
        // In its own commit, deliberately: the delivery has to survive a business failure, because
        // "we received it and could not act on it" is a state an operator can find.
        //
        // The recorder also decides what a redelivery may do. The unique index still refuses a
        // second row, but a delivery whose earlier attempt failed or died is claimed and processed
        // again - that retry is what the money step's rollback relies on.
        var outcome = await WebhookEventRecorder.RecordAsync(_context, command, _logger, cancellationToken);

        if (outcome.Decision != WebhookRecordDecision.Process)
        {
            _metrics.MonobankWebhookDuplicate(
                outcome.Decision == WebhookRecordDecision.InFlight ? "in_flight" : "redelivery");

            // Acknowledged, not processed. Monobank retries on a non-2xx, so anything else here turns
            // one redelivery into a loop against an event we already acted on - or, when another
            // attempt still holds the lease, into a second attempt at the same money.
            return new ProcessMonobankWebhookResponse
            {
                Success = true,
                Message = outcome.Decision == WebhookRecordDecision.InFlight
                    ? "Duplicate delivery is already being processed"
                    : "Duplicate delivery already recorded"
            };
        }

        var stored = outcome.Stored!;

        ProcessingOutcome processed;
        try
        {
            processed = await RunMoneyStepAsync(command, stored, cancellationToken);
        }
        catch (Exception ex)
        {
            // The delivery is on file and still marked outstanding, which is the point: "we received
            // it and could not act on it" is a state an operator can find, and it is the opposite of
            // the delivery silently disappearing. Counted as a failure here because this path has no
            // other metric - the row's own error column is only visible to someone already looking.
            _metrics.MonobankWebhookFailed("exception");

            await WebhookEventRecorder.CompleteAsync(
                _context, stored, WebhookReceiptStatus.Failed, ex.Message, _logger, cancellationToken);
            throw;
        }

        // Only after the money step is committed. A notification that announces a payment the
        // transaction rolled back is a lie an operator has to disprove, and a fulfilment queued
        // before the commit can start before there is anything to fulfil.
        await AnnounceAsync(command, processed, cancellationToken);

        await WebhookEventRecorder.CompleteAsync(
            _context, stored, ReceiptStatusFor(processed.Response), ErrorFor(processed.Response), _logger, cancellationToken);

        return processed.Response;
    }

    /// <summary>
    /// Acknowledges a delivery that moved nothing: the order is where it was, and no payment is
    /// announced and no fulfilment is queued for it.
    /// </summary>
    private static ProcessingOutcome Ack(Order order, string message) => new(
        new ProcessMonobankWebhookResponse
        {
            Success = true,
            OrderId = order.Id.ToString(),
            PreviousStatus = order.Status.ToString(),
            NewStatus = order.Status.ToString(),
            Message = message
        },
        order,
        TransitionedTo: null);
}
