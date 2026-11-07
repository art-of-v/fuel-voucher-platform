using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Payments.Admin.DismissPayment;

/// <summary>
/// Closes money off the worklist without touching the money.
/// </summary>
/// <remarks>
/// <para>
/// The worklist is a list of questions — whose is this, which order does it pay — and not every row has
/// an answer. A one-hryvnia test capture, a transfer from before the payer registered, a rounding
/// charge: the money is real, the acquirer took it and the journal booked it, and it will never be
/// placed on a sale. Deleting the row would be the wrong fix twice over: the acquisition statement
/// would then show a received capture no payment row holds — the exact alarm
/// <c>UnappliedReceipts</c> raises — and the cash in the books would no longer match the bank.
/// </para>
/// <para>
/// So the row stays and the question is answered: seen, closed, will never be placed. The payment, its
/// journal entry and its statement row are untouched; only the operator's queue forgets it. The reason
/// is required because "why is this money not on the list" is a question somebody asks later, and the
/// act is audited for the same reason.
/// </para>
/// </remarks>
public sealed class DismissPaymentCommandHandler
{
    /// <summary>Matches the column. A reason longer than this is a note, and notes belong in the audit row.</summary>
    public const int MaxReasonLength = 200;

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;
    private readonly ILogger<DismissPaymentCommandHandler> _logger;

    public DismissPaymentCommandHandler(
        ApplicationDbContext context,
        AuditEventService events,
        ILogger<DismissPaymentCommandHandler> logger)
    {
        _context = context;
        _events = events;
        _logger = logger;
    }

    public async Task<DismissPaymentResult> HandleAsync(
        DismissPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var reason = command.Reason?.Trim();

        if (string.IsNullOrEmpty(reason))
            return DismissPaymentResult.Refused("Say why this money will never be placed.");

        if (reason.Length > MaxReasonLength)
            return DismissPaymentResult.Refused($"The reason is longer than {MaxReasonLength} characters.");

        // AsTracking, stated rather than inherited: under a default of NoTracking this would be a
        // silent no-op that still answered 204. NoTrackingMutationGuardTests enforces the pattern.
        var payment = await _context.Payments
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, cancellationToken);
        if (payment is null) return DismissPaymentResult.NotFound();

        // Only money we actually hold appears on the worklist. A pending or failed row is not money,
        // and dismissing an invoice that nobody paid would hide the state the reconciliation report is
        // about to act on.
        if (payment.Status != PaymentStatus.Succeeded)
        {
            return DismissPaymentResult.Refused(
                $"Only succeeded money can be dismissed. This payment is {payment.Status}.");
        }

        if (payment.OrderId is not null)
        {
            // Money on an order is not held money: it is not on the worklist to begin with, so a
            // dismissal here would look like it changed something and change nothing.
            return DismissPaymentResult.Refused(
                "This payment is on an order; the worklist does not hold it.");
        }

        if (payment.DismissedAtUtc is not null)
        {
            // The same press again. Answered rather than refused, because a double-click should not
            // look like a failure — and the first reason stands, not this one.
            return DismissPaymentResult.Done();
        }

        var before = JsonSerializer.Serialize(new
        {
            dismissed_at_utc = (string?)null,
            dismissed_reason = (string?)null,
        });

        payment.DismissedAtUtc = SystemClock.UtcNow;
        payment.DismissedReason = reason;
        payment.DismissedByUserId = command.ActorId;
        payment.UpdatedAtUtc = SystemClock.UtcNow;

        var after = JsonSerializer.Serialize(new
        {
            dismissed_at_utc = payment.DismissedAtUtc,
            dismissed_reason = reason,
        });

        // The audit row is tracked before the save, so it commits in the same transaction as the
        // dismissal it describes.
        Record(payment, before, after, command, reason);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment {PaymentId} ({Amount} {Currency}, invoice {InvoiceId}) dismissed from the worklist: {Reason}",
            payment.Id, payment.AmountUah, payment.Currency, payment.ProviderInvoiceId, reason);

        return DismissPaymentResult.Done();
    }

    private void Record(
        Payment payment,
        string before,
        string after,
        DismissPaymentCommand command,
        string reason)
    {
        // No actor, no record: an audit row that cannot name who closed money names nothing.
        if (command.ActorId is null) return;

        _events.RecordEvent(
            "Payment", payment.Id.ToString(),
            "PaymentDismissed", before, after,
            command.ActorId.Value, command.ActorName,
            $"Dismissed {payment.AmountUah:N2} {payment.Currency} from the worklist: {reason}",
            "all");
    }
}
