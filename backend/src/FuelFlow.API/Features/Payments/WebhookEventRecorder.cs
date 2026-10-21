using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Payments;

/// <summary>What <see cref="WebhookEventRecorder.RecordAsync"/> did with a delivery.</summary>
/// <param name="Stored">The stored row, or null when the delivery was a redelivery.</param>
/// <param name="IsRedelivery">
/// True when the unique index refused the insert, which is the authoritative signal that this exact
/// provider event is already on file. Nothing downstream runs in that case.
/// </param>
internal readonly record struct WebhookRecordOutcome(WebhookEvent? Stored, bool IsRedelivery);

/// <summary>
/// Stores every provider delivery before anything acts on it.
/// </summary>
/// <remarks>
/// <para>
/// The write happens first, on purpose. A delivery that arrives, is stored, and then fails during
/// processing leaves a <see cref="WebhookReceiptStatus.Received"/> row - which is the useful
/// outcome, because the next question is always "did they tell us?". Writing afterwards would record
/// only the successes, which is exactly the class of event nobody needs recorded.
/// </para>
/// <para>
/// <b>Why the unique index and not a lookup.</b> Checking for an existing row and then inserting is a
/// race: two deliveries of the same event can both read "not present" and both insert. The unique
/// index makes the second one impossible, so the authoritative signal is Postgres answering 23505.
/// There is deliberately no pre-read - it would only avoid raising that exception in the common
/// case, at the cost of implying the lookup is what enforces uniqueness.
/// </para>
/// <para>
/// Static rather than injected: it is persistence against a context the caller already owns, and
/// registering it would have meant a new constructor argument on the handler and on every test that
/// builds one.
/// </para>
/// </remarks>
internal static class WebhookEventRecorder
{
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
            ReceivedAtUtc = SystemClock.UtcNow,
            Attempts = 1
        };

        context.WebhookEvents.Add(stored);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new WebhookRecordOutcome(stored, false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The authoritative duplicate signal. Detach the row that lost the race and count the
            // delivery on the one that won: "the acquirer sent this twice" is an answer, and until
            // this table existed nothing anywhere recorded it.
            context.Entry(stored).State = EntityState.Detached;

            logger.LogInformation(
                "Redelivery of Monobank event {ProviderEventId} for invoice {InvoiceId}; already recorded",
                providerEventId, command.InvoiceId);

            await CountRedeliveryAsync(context, WebhookEventIdentity.Monobank, providerEventId, cancellationToken);

            return new WebhookRecordOutcome(null, true);
        }
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