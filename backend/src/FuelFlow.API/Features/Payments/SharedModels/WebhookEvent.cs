using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Payments.SharedModels;

/// <summary>
/// One delivery from a payment provider, stored verbatim the moment it arrives.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this fills.</b> Until now the webhook body was read in
/// <c>MonobankWebhookController</c>, handed to the handler, and dropped. The handler never read
/// <c>RawBody</c> and never wrote it anywhere, and the controller's success log carries the order
/// id and the new status rather than the payload. So the single authoritative statement that a
/// customer paid - the acquirer's own signed document - survived as long as the request did. When
/// a customer disputes a charge a month later, there is nothing to show: not the amount the brand
/// reported, not the two deliveries of the same event, not the one that arrived out of order.
/// </para>
///
/// <para>
/// <b>Why it is written before processing, not after.</b> The row is inserted first, then the order
/// transition runs, then the row is marked. A crash between the two leaves
/// <see cref="WebhookReceiptStatus.Received"/> rather than nothing - which is the whole point: the
/// delivery is evidence regardless of whether we could act on it. Writing it afterwards would
/// record only our successes, which is precisely the class of event nobody needs recorded.
/// </para>
///
/// <para>
/// <b>Identity.</b> Monobank's invoice webhook carries no event id - the body is status, amount,
/// createdDate, modifiedDate, invoiceId and cancelList - so
/// <see cref="ProviderEventId"/> is derived from the fields that identify the event semantically.
/// Deriving it from the raw bytes instead would miss a redelivery whose key order or whitespace
/// differs; deriving it from <c>modifiedDate</c> alone would collide two events in the same second.
/// See <c>WebhookEventIdentity.Derive</c> for the exact rule and what it costs.
/// </para>
///
/// <para>
/// <b>The unique index is the idempotency guarantee.</b> A redelivery is rejected by Postgres
/// (23505) rather than by a handler deciding it looks like a duplicate. That is the difference
/// between "the application judged this to be a replay" and "this exact event cannot exist twice".
/// The index refuses a second <em>row</em>, not a retry of the first: a delivery that failed can
/// claim the outstanding row through <see cref="ProcessingStartedAtUtc"/> and run again, which is
/// the retry the money step's rollback relies on.
/// </para>
/// </remarks>
public sealed class WebhookEvent
{
    public Guid Id { get; set; }

    /// <summary>The vendor token, lowercase: <c>monobank</c>.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The provider's identity for this event, or our stable derivation of it where the provider
    /// supplies none. Unique with <see cref="Provider"/>: that pair is what makes a redelivery
    /// impossible rather than merely ignored.
    /// </summary>
    public string ProviderEventId { get; set; } = null!;

    /// <summary>
    /// The acquirer's status word - <c>success</c>, <c>failure</c>, <c>reversed</c>, <c>expired</c> -
    /// in its own vocabulary, kept exactly as sent rather than mapped onto our status enum. The
    /// whole reason a stored event is worth storing is that it says what they said.
    /// </summary>
    public string ProviderStatus { get; set; } = null!;

    /// <summary>The acquirer's invoice id. Indexed, because every question about an event starts there.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Kopecks, as reported. Evidence of what was charged, independent of our price.</summary>
    public decimal? AmountKopecks { get; set; }

    public DateTime? ProviderCreatedAtUtc { get; set; }
    public DateTime? ProviderModifiedAtUtc { get; set; }

    /// <summary>The body exactly as received.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>The signature header, so a stored delivery can be re-verified later without the request.</summary>
    public string? Signature { get; set; }

    public string? KeyId { get; set; }

    [MutableAfterInsert]
    public WebhookReceiptStatus Status { get; set; } = WebhookReceiptStatus.Received;

    /// <summary>When we received it. Our clock, and the one thing here we are the authority on.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>When processing finished. Null while a delivery is still outstanding.</summary>
    [MutableAfterInsert]
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// When the current processing attempt started, held as a lease: a redelivery may take over
    /// only once this is null or older than the lease. Null means no attempt is running.
    /// </summary>
    /// <remarks>
    /// Without it a redelivery cannot tell "the first attempt is still running" from "the process
    /// that took it died", and refusing both is exactly the defect: a delivery whose money step
    /// rolled back could never be tried again. The lease answers both without a new status - a
    /// fresh claim is in flight, a stale one is abandoned and may be retried.
    /// </remarks>
    [MutableAfterInsert]
    public DateTime? ProcessingStartedAtUtc { get; set; }

    /// <summary>Deliveries seen for this event. Above 1 means the acquirer retried us.</summary>
    [MutableAfterInsert]
    public int Attempts { get; set; } = 1;

    /// <summary>Why processing failed, when it did.</summary>
    [MutableAfterInsert]
    public string? Error { get; set; }
}

public enum WebhookReceiptStatus
{
    /// <summary>Stored, not yet acted on.</summary>
    Received,

    /// <summary>Acted on.</summary>
    Processed,

    /// <summary>
    /// Deliberately not acted on - a replay, an unrecognised status, or a transition the state
    /// machine refused. Recorded rather than dropped, because "we saw it and decided against it"
    /// is an answer and "we never saw it" is not.
    /// </summary>
    Ignored,

    /// <summary>Processing threw. The delivery exists and needs attention.</summary>
    Failed
}