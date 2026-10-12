namespace FuelFlow.API.BackgroundJobs.Models;

/// <summary>
/// Payload of the <c>VOUCHER_ISSUED</c> outbox event (#150 W3).
/// <para>
/// Deliberately not an order id. A worker is handed fuel by their employer; the order belongs to
/// the company, and carrying it would invite a deep link to a screen the worker has no access to.
/// <see cref="LegalEntityId"/> is what identifies the context to open instead — which matters now
/// that a worker can belong to several companies (W1).
/// </para>
/// </summary>
public sealed class VoucherIssuedPayload
{
    /// <summary>The worker who was issued the fuel.</summary>
    public string WorkerUserId { get; set; } = null!;

    /// <summary>The company the fuel came from, for the notification's wording.</summary>
    public string? CompanyName { get; set; }

    /// <summary>How many vouchers were issued in this batch.</summary>
    public int VoucherCount { get; set; }

    /// <summary>
    /// The company whose worker context a tapped push should open. Optional because a company can be
    /// deleted while an event is still queued, and a notification with no company to name is still
    /// worth sending.
    /// </summary>
    public string? LegalEntityId { get; set; }
}