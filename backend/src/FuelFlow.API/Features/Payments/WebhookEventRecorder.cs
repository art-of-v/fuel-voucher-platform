using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Payments;

/// <summary>What <see cref="WebhookEventRecorder.RecordAsync"/> decided about a delivery.</summary>
internal enum WebhookRecordDecision
{
    /// <summary>The delivery is on file and this caller owns the attempt; the handler processes it.</summary>
    Process,

    /// <summary>
    /// The delivery was already settled - processed, ignored, or refused by the business rules - so
    /// running it again could only duplicate an effect that has already happened.
    /// </summary>
    AlreadyActedOn,

    /// <summary>
    /// Another attempt at this delivery holds the lease and has not finished. This caller must not
    /// start a second one; the running attempt will answer the acquirer.
    /// </summary>
    InFlight
}

/// <summary>What <see cref="WebhookEventRecorder.RecordAsync"/> did with a delivery.</summary>
/// <param name="Stored">
/// The stored row, or null when the caller must not process the delivery. On a retry this is the
/// row the earlier attempt left behind, not a new one.
/// </param>
/// <param name="Decision">Whether the caller processes the delivery, and if not, why.</param>
internal readonly record struct WebhookRecordOutcome(WebhookEvent? Stored, WebhookRecordDecision Decision);

/// <summary>
/// Stores every provider delivery before anything acts on it.
/// </summary>
/// <remarks>
/// <para>
/// The write happens first, on purpose. A delivery that arrives, is stored, and then fails during
/// processing leaves an outstanding row - <see cref="WebhookReceiptStatus.Received"/> when the
/// attempt never finished, <see cref="WebhookReceiptStatus.Failed"/> when it finished badly - which
/// is the useful outcome, because the next question is always "did they tell us?". Writing
/// afterwards would record only the successes, which is exactly the class of event nobody needs
/// recorded.
/// </para>
/// <para>
/// <b>Why the unique index and not a lookup.</b> Checking for an existing row and then inserting is a
/// race: two deliveries of the same event can both read "not present" and both insert. The unique
/// index makes the second one impossible, so the authoritative signal is Postgres answering 23505.
/// There is deliberately no pre-read - it would only avoid raising that exception in the common
/// case, at the cost of implying the lookup is what enforces uniqueness.
/// </para>
/// <para>
/// <b>A redelivery is not automatically a replay.</b> The unique index refuses a second row, which
/// is right, but the delivery it refused may be the acquirer retrying one whose money step rolled
/// back - that row is Failed and has never advanced the order. So the violation now decides:
/// a settled delivery is refused, an outstanding one whose attempt is still running is refused as
/// in flight, and an outstanding one whose attempt died or failed is claimed and processed. The
/// claim is a single conditional UPDATE against a lease column, so Postgres - not the handler -
/// picks the one caller that gets to retry.
/// </para>
/// <para>
/// Static rather than injected: it is persistence against a context the caller already owns, and
/// registering it would have meant a new constructor argument on the handler and on every test that
/// builds one.
/// </para>
/// </remarks>
internal static class WebhookEventRecorder
{
    /// <summary>
    /// How long one attempt owns a delivery before a redelivery may take it over.
    /// </summary>
    /// <remarks>
    /// Long enough that a money step doing an outbound refund sync is not overtaken by a second
    /// delivery, short enough that a process that died mid-attempt is retried by the next acquirer
    /// redelivery or reconciliation poll instead of being stuck forever. An attempt that finishes
    /// clears the lease in <see cref="CompleteAsync"/>, so the window only matters when it does not.
    /// </remarks>
    internal static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    internal static async Task<WebhookRecordOutcome> RecordAsync(
        ApplicationDbContext context,
        ProcessMonobankWebhookCommand command,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var providerEventId = WebhookEventIdentity.Derive(
            WebhookEventIdentity.Monobank,
            command.InvoiceId,
            command.Status,
            command.ModifiedDate);

        // Checked before the row is built rather than after it is added. WebhookEventIdentity returns
        // null rather than inventing an id for an event it cannot identify, and
        // ck_webhook_events_event_id_length rejects a row without one - so this is a hard failure by
        // design, not a silent gap: a delivery with no invoice and no timestamp cannot be
        // deduplicated and must not be pretended otherwise.
        if (providerEventId is null)
        {
            logger.LogWarning(
                "Monobank delivery for invoice {InvoiceId} has no derivable event id and cannot be stored",
                command.InvoiceId);

            throw new InvalidOperationException(
                "A Monobank delivery with neither an invoice id nor a modified date cannot be identified, " +
                "so it cannot be recorded without weakening ux_webhook_events_provider_event.");
        }

        var now = SystemClock.UtcNow;
        var stored = new WebhookEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = WebhookEventIdentity.Monobank,
            ProviderEventId = providerEventId,
            ProviderStatus = command.Status,
            InvoiceId = command.InvoiceId,
            AmountKopecks = command.Amount,
            ProviderCreatedAtUtc = command.CreatedDate,
            ProviderModifiedAtUtc = command.ModifiedDate,
            Payload = string.IsNullOrWhiteSpace(command.RawBody) ? "{}" : command.RawBody,
            Signature = command.Signature,
            KeyId = command.KeyId,
            Status = WebhookReceiptStatus.Received,
            ReceivedAtUtc = now,
            ProcessingStartedAtUtc = now,
            Attempts = 1
        };

        context.WebhookEvents.Add(stored);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new WebhookRecordOutcome(stored, WebhookRecordDecision.Process);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The authoritative duplicate signal. Detach the row that lost the race: the one already
            // on file is the delivery's identity, and what happens next depends on what that row
            // has already done.
            context.Entry(stored).State = EntityState.Detached;

            logger.LogInformation(
                "Redelivery of Monobank event {ProviderEventId} for invoice {InvoiceId}; checking whether it is retryable",
                providerEventId, command.InvoiceId);

            return await DecideRedeliveryAsync(context, providerEventId, now, logger, cancellationToken);
        }
    }

    /// <summary>
    /// Decides what a delivery the unique index refused may do, and claims it for this attempt when
    /// the answer is "retry".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim is one conditional UPDATE. It matches only a row that is still outstanding
    /// (<c>Received</c> or <c>Failed</c>) and has no live lease - either no attempt ever finished
    /// clearing it, or the attempt that held it is older than <see cref="ProcessingLease"/>. Two
    /// redeliveries racing for the same failed event both run this statement; the first commits a
    /// fresh lease, the second re-evaluates the predicate against the committed row, matches nothing
    /// and is refused. That is what keeps "process the retry" from becoming "process it twice".
    /// </para>
    /// <para>
    /// A refused delivery still counts as an attempt, because "the acquirer sent this twice" is an
    /// answer and was previously recorded nowhere.
    /// </para>
    /// </remarks>
    private static async Task<WebhookRecordOutcome> DecideRedeliveryAsync(
        ApplicationDbContext context,
        string providerEventId,
        DateTime now,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var leaseExpiredBefore = now - ProcessingLease;

        var claimed = await context.WebhookEvents
            .Where(e => e.Provider == WebhookEventIdentity.Monobank
                     && e.ProviderEventId == providerEventId
                     && (e.Status == WebhookReceiptStatus.Received || e.Status == WebhookReceiptStatus.Failed)
                     && (e.ProcessingStartedAtUtc == null || e.ProcessingStartedAtUtc < leaseExpiredBefore))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.ProcessingStartedAtUtc, now)
                    .SetProperty(e => e.Attempts, e => e.Attempts + 1),
                cancellationToken);

        var existing = await context.WebhookEvents
            .FirstOrDefaultAsync(
                e => e.Provider == WebhookEventIdentity.Monobank && e.ProviderEventId == providerEventId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Monobank delivery {providerEventId} lost the unique index but could not be read back.");

        if (claimed == 1)
        {
            // The claim UPDATE bypassed the tracker. Reload so a row this context already tracked
            // from an earlier attempt carries the claim and attempt count the database now holds -
            // without it, CompleteAsync would not write the cleared claim, because EF only sends
            // properties whose in-memory value changed.
            await context.Entry(existing).ReloadAsync(cancellationToken);

            logger.LogInformation(
                "Retrying Monobank event {ProviderEventId} for invoice {InvoiceId}: the earlier attempt did not settle it",
                providerEventId, existing.InvoiceId);

            return new WebhookRecordOutcome(existing, WebhookRecordDecision.Process);
        }

        await CountRedeliveryAsync(context, WebhookEventIdentity.Monobank, providerEventId, cancellationToken);

        var inFlight = existing.ProcessingStartedAtUtc is not null
            && existing.ProcessingStartedAtUtc >= leaseExpiredBefore;

        logger.LogInformation(
            "Monobank event {ProviderEventId} refused as {Decision}: status {Status}, attempt started {StartedAt}",
            providerEventId,
            inFlight ? nameof(WebhookRecordDecision.InFlight) : nameof(WebhookRecordDecision.AlreadyActedOn),
            existing.Status,
            existing.ProcessingStartedAtUtc);

        return new WebhookRecordOutcome(
            null, inFlight ? WebhookRecordDecision.InFlight : WebhookRecordDecision.AlreadyActedOn);
    }

    /// <summary>
    /// Marks a stored delivery with its outcome.
    /// </summary>
    /// <remarks>
    /// Best-effort by design, and it has to be: the caller has already decided the payment path's
    /// outcome, and reporting that as a failure because the bookkeeping about it failed would turn a
    /// successful payment into a 500. A delivery stuck on <c>Received</c> is visible through the
    /// partial index and is recoverable by hand; a customer charged and served an error is not.
    /// </remarks>
    public static async Task CompleteAsync(
        ApplicationDbContext context,
        WebhookEvent stored,
        WebhookReceiptStatus status,
        string? error,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        stored.Status = status;
        stored.ProcessedAtUtc = SystemClock.UtcNow;
        // The attempt is over either way - processed, ignored or failed. Clearing the lease is what
        // lets a later redelivery of a failed event claim it; a row that keeps a lease while settling
        // would answer "in flight" to every retry until the lease aged out for no reason.
        stored.ProcessingStartedAtUtc = null;
        stored.Error = error is null ? null : error[..Math.Min(error.Length, 1000)];

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex,
                "Could not record the processing outcome for webhook event {WebhookEventId}; it stays outstanding",
                stored.Id);
        }
    }

    private static async Task CountRedeliveryAsync(
        ApplicationDbContext context,
        string provider,
        string providerEventId,
        CancellationToken cancellationToken)
    {
        // A tracked read-modify-write rather than a raw UPDATE, for two reasons. It keeps the
        // in-memory provider usable, which is what the unit tests run on; and it avoids raising a
        // concurrency exception on a payment path for a counter nobody computes with. Two truly
        // simultaneous redeliveries can lose one increment between them. That is acceptable: the
        // number exists so a human investigating "did they send it twice" can see that they did, and
        // it is still 2 either way.
        var existing = await context.WebhookEvents
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ProviderEventId == providerEventId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.Attempts += 1;
        await context.SaveChangesAsync(cancellationToken);
    }
}