using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Orders.RefundOrder;

/// <summary>
/// Books a confirmed refund the confirmation path could not: the operator's door to the same two legs.
/// </summary>
/// <remarks>
/// <para>
/// The refund is normally booked by <see cref="RefundJournalPoster"/> the moment the acquirer confirms
/// it. That posting can be refused - the period may be closed, a mapping rule may be missing, the
/// database may refuse - and the confirmation path logs it and carries on, because a refund the bank
/// has already made cannot wait for the books. Nothing used to ask that refund again: the sync only
/// picks up refunds still <c>Pending</c>, and this one is <c>Succeeded</c>.
/// </para>
/// <para>
/// So this is the second door, and it is deliberately the <em>same</em> poster rather than a
/// hand-composed entry: the keys, the amounts, the date and the VAT cap are the ones the confirmation
/// would have written. Pressing it twice writes one fact, because the poster is idempotent on the
/// refund's id - a replay is safe and answers with what is booked.
/// </para>
/// <para>
/// It refuses a payment that is not a refund, and a refund the acquirer has not confirmed: booking
/// money that was only asked back would put a return in the books ahead of the bank. This is a
/// repair for a confirmation that happened, not a way to pre-book one.
/// </para>
/// </remarks>
public sealed record ReDriveRefundPostingCommand(Guid RefundPaymentId);

/// <param name="RefundUah">What the gross leg now holds in the books for this refund.</param>
/// <param name="VatReleasedUah">What the VAT leg now holds; zero when the receipt carved none.</param>
public sealed record ReDriveRefundPostingResult(
    Guid RefundPaymentId,
    Guid OrderId,
    decimal RefundUah,
    decimal VatReleasedUah);

public sealed class ReDriveRefundPostingCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly RefundJournalPoster _poster;
    private readonly ILogger<ReDriveRefundPostingCommandHandler> _logger;

    public ReDriveRefundPostingCommandHandler(
        ApplicationDbContext context,
        RefundJournalPoster poster,
        ILogger<ReDriveRefundPostingCommandHandler> logger)
    {
        _context = context;
        _poster = poster;
        _logger = logger;
    }

    public async Task<ReDriveRefundPostingResult> HandleAsync(
        ReDriveRefundPostingCommand command,
        CancellationToken cancellationToken = default)
    {
        var refund = await _context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.RefundPaymentId, cancellationToken)
            ?? throw new LedgerPostingException(
                $"Payment {command.RefundPaymentId} does not exist.");

        if (refund.Kind != PaymentKind.Refund)
            throw new LedgerPostingException(
                $"Payment {refund.Id} is a {refund.Kind}; only a Refund may book "
                + $"'{RefundJournalPoster.RefundedEvent}'.");

        if (refund.Status != PaymentStatus.Succeeded)
            throw new LedgerPostingException(
                $"Refund {refund.Id} is {refund.Status}; the acquirer has not confirmed the money went "
                + "back, so there is nothing to book yet.");

        if (refund.OrderId is not { } orderId)
            throw new LedgerPostingException(
                $"Refund {refund.Id} names no order; a refund is booked against the sale it returns.");

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new LedgerPostingException(
                $"Order {orderId} is not in view; refund {refund.Id} cannot be booked against it.");

        await _poster.BookConfirmedRefundAsync(refund, order, cancellationToken);

        var refunded = await CarvedVat.PostedAmountAsync(
            _context, RefundJournalPoster.RefundedEvent, JournalSourceType.Refund, refund.Id,
            cancellationToken);
        var vatReleased = await CarvedVat.PostedAmountAsync(
            _context, RefundJournalPoster.RefundedVatEvent, JournalSourceType.Refund, refund.Id,
            cancellationToken);

        _logger.LogInformation(
            "REFUND LEDGER: operator re-drove refund {RefundId} (order {OrderId}); the books hold "
            + "{Refund:0.00} UAH returned and {Vat:0.00} UAH of VAT released.",
            refund.Id, orderId, refunded, vatReleased);

        return new ReDriveRefundPostingResult(refund.Id, orderId, refunded, vatReleased);
    }
}
