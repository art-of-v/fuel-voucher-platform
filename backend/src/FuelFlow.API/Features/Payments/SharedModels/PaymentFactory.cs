using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.Payments.SharedModels;

/// <summary>
/// Builds and moves <see cref="Payment"/> rows, so the three write paths that touch money cannot
/// drift apart on status vocabulary or unit conversion.
/// </summary>
/// <remarks>
/// The amount conversion goes through <see cref="Money"/> exactly once, here, and the database
/// enforces the result with <c>ck_payments_amount_units_agree</c>. Between those two, "UAH at
/// checkout" and "kopecks on the wire" cannot become two different numbers for one payment.
/// </remarks>
public static class PaymentFactory
{
    /// <summary>
    /// A pending capture for an invoice that has just been created at the acquirer. The order's own
    /// price is both amounts: it is what we billed, and by construction it is what was sent on the
    /// wire, so a later webhook reporting a different figure is a real finding rather than a
    /// conversion artefact.
    /// </summary>
    public static Payment CreateCapture(
        Order order,
        MonobankMerchant merchant,
        string providerInvoiceId,
        DateTime nowUtc)
    {
        return new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            Merchant = merchant,
            ProviderInvoiceId = providerInvoiceId,
            AmountUah = order.Price,
            AmountKopecks = Money.ToKopecks(order.Price),
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// A pending refund against a capture, carrying the approval that authorised it.
    /// </summary>
/// <remarks>
/// <para>
/// <paramref name="id"/> is the refund's own id, not a fresh one, and that is the point rather than
/// an optimisation. While <c>refunds</c> is still a table this makes the refund payment and the refund
/// row one identity under two names, so a caller holding a refund id - a notification, an API
/// response, the status sync - finds the payment by it directly. It also means that when
/// <c>refunds</c> becomes a view over this table, the view's <c>id</c> is already what every caller
/// passes around, and nothing has to be remapped at that point. Minting a separate id would have kept
/// a <c>refund_id</c> back-reference alive for the whole migration, which is the circular dependency
/// this exists to remove.
/// </para>
/// <para>
/// The money left via the same acquirer invoice the capture came in on, so
/// <paramref name="providerInvoiceId"/> is the order's invoice rather than a new one - and the
/// partial index over <c>(provider, provider_invoice_id, kind)</c> is what keeps that honest: the
/// capture and the refund share the invoice but not the kind, so they coexist instead of
/// colliding.
/// </para>
/// <para>
/// <b>The amount is deliberately the refund's, not the capture's.</b> A refund can correct a price
/// downwards and return less than was taken, so this is passed in rather than derived from
/// <paramref name="capture"/>. It is still validated against the capture's own amount by the
/// caller's arithmetic - the factory records what it was told, and the order's refund computation
/// owns whether that number is right.
/// </para>
/// <para>
/// The approval travels with the payment because once <c>refunds</c> becomes a view over this
/// table there is nowhere else for it to live.
/// </para>
/// </remarks>
public static Payment CreateRefund(
        Guid id,
        Order order,
        Payment capture,
        decimal amountKopecks,
        Guid? createdByUserId,
        string? createdByUserName,
        bool isAutomatic,
        DateTime nowUtc)
    {
        return new Payment
        {
            Id = id,
            OrderId = order.Id,
            Kind = PaymentKind.Refund,
            ParentPaymentId = capture.Id,
            Provider = WebhookEventIdentity.Monobank,
            Merchant = capture.Merchant,
            ProviderInvoiceId = capture.ProviderInvoiceId,
            AmountUah = Money.FromKopecks(amountKopecks),
            AmountKopecks = amountKopecks,
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedByUserId = createdByUserId,
            CreatedByUserName = createdByUserName,
            IsAutomatic = isAutomatic,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// The acquirer's status word as a <see cref="PaymentStatus"/>, or <see langword="null"/> when
    /// we have no mapping for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>reversed</c> maps to <see cref="PaymentStatus.Cancelled"/> and not to a failure: money was
    /// taken and has been given back, and recording it as never-collected would understate it.
    /// The refund row is the authority for the rest.
    /// </para>
    /// <para>
    /// An unmapped word returns null so the caller records what the acquirer said rather than
    /// guessing. That is the whole reason the provider's vocabulary is kept verbatim on
    /// <see cref="WebhookEvent.ProviderStatus"/>.
    /// </para>
    /// </remarks>
    public static PaymentStatus? FromProviderStatus(string? providerStatus) => providerStatus?.Trim()
        .ToLowerInvariant() switch
        {
            "success" => PaymentStatus.Succeeded,
            "failure" => PaymentStatus.Failed,
            "reversed" => PaymentStatus.Cancelled,
            "expired" => PaymentStatus.Expired,
            _ => null
        };

    /// <summary>The order's recorded view of the payment, as a payment status.</summary>
    public static PaymentStatus FromOrderStatus(MonobankStatus? status) => status switch
    {
        MonobankStatus.Success => PaymentStatus.Succeeded,
        MonobankStatus.Failure => PaymentStatus.Failed,
        MonobankStatus.Cancelled => PaymentStatus.Cancelled,
        MonobankStatus.Expired => PaymentStatus.Expired,
        // Pending, and null, both mean an invoice exists and nobody has paid it.
        _ => PaymentStatus.Pending
    };

    /// <summary>
    /// Whether moving this payment would contradict a capture the acquirer already reported as paid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A succeeded capture does not become failed or expired. Money that was taken cannot un-be taken
    /// by a later status word: only a refund returns it, and that is a row of its own.
    /// </para>
    /// <para>
    /// The case this exists for is a late <c>failure</c> webhook for an invoice that was already paid.
    /// The order refuses that transition and records the acquirer's word instead
    /// (<c>RecordUnappliedPaymentTruthAsync</c>); the payment used to follow it anyway, ending up
    /// saying nobody had paid while the journal entry for the money stayed.
    /// </para>
    /// <para>
    /// <c>reversed</c> is deliberately not covered: it means money that was taken and returned, so
    /// <see cref="PaymentStatus.Cancelled"/> is the truthful end state rather than a contradiction.
    /// </para>
    /// </remarks>
    public static bool WouldUndoACapture(Payment payment, PaymentStatus status) =>
        payment.Kind == PaymentKind.Capture
        && payment.Status == PaymentStatus.Succeeded
        && status is PaymentStatus.Failed or PaymentStatus.Expired;

    /// <summary>
    /// Moves a payment to a new status and stamps the time the money moved.
    /// </summary>
    /// <remarks>
    /// <b>The first paid-at wins.</b> A payment that already has one is not re-stamped by a later
    /// callback: the webhook's modification date moves every time the acquirer touches the invoice,
    /// and overwriting would replace the moment the money actually arrived with the moment someone
    /// last edited the record. That is the same reason
    /// <see cref="Order.LastWebhookModifiedDateUtc"/> is the provider's date and not
    /// <c>UtcNow</c>.
    /// </remarks>
    /// <returns>
    /// Whether the row moved. <see langword="false"/> when it already had that status or the move is
    /// refused by <see cref="WouldUndoACapture"/>, so the caller can say why in its own log.
    /// </returns>
    public static bool ApplyStatus(
        Payment payment,
        PaymentStatus status,
        DateTime? observedAtUtc,
        DateTime nowUtc)
    {
        if (payment.Status == status || WouldUndoACapture(payment, status))
        {
            return false;
        }

        payment.Status = status;
        payment.UpdatedAtUtc = nowUtc;

        if (payment.PaidAtUtc is null && observedAtUtc is not null && status is PaymentStatus.Succeeded)
        {
            payment.PaidAtUtc = observedAtUtc;
        }

        return true;
    }
}