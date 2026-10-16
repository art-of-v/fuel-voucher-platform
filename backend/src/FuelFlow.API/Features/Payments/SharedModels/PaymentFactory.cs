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
    public static void ApplyStatus(
        Payment payment,
        PaymentStatus status,
        DateTime? observedAtUtc,
        DateTime nowUtc)
    {
        if (payment.Status == status)
        {
            return;
        }

        payment.Status = status;
        payment.UpdatedAtUtc = nowUtc;

        if (payment.PaidAtUtc is null && observedAtUtc is not null && status is PaymentStatus.Succeeded)
        {
            payment.PaidAtUtc = observedAtUtc;
        }
    }
}