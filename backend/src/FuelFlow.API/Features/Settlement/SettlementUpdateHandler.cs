using FuelFlow.Features.Feedback;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settlement.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Settlement;

/// <summary>
/// Turns one Telegram message into money, or into a recorded reason why it did not.
/// </summary>
/// <remarks>
/// <para>
/// The order of the checks is the design. Authorisation, then idempotency, then parsing, then
/// resolution, then attaching — each one narrowing what can happen next, and none of them guessing.
/// </para>
/// <para>
/// <b>Nothing here infers.</b> A code that matches nobody, two orders that match, or an amount that
/// disagrees with the one open order all produce the same outcome: a real payment with no order on
/// it, recorded and left for an operator. A wrong guess here credits money to the wrong person or
/// delivers fuel to the wrong one, and neither failure is recoverable from the ledger afterwards.
/// </para>
/// </remarks>
public sealed class SettlementUpdateHandler
{
    private readonly ApplicationDbContext _context;
    private readonly TelegramSettlementOptions _options;
    private readonly ILogger<SettlementUpdateHandler> _logger;

    public SettlementUpdateHandler(
        ApplicationDbContext context,
        IOptions<TelegramSettlementOptions> options,
        ILogger<SettlementUpdateHandler> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        var message = update.Message;
        if (message?.Text is null || message.From is null || message.Chat is null)
        {
            return;
        }

        if (!_options.IsConfigured)
        {
            // Not an error: the bot is off, or not yet given its chat and allow list. Writing a row
            // here would fill the table with messages from a channel we are not serving.
            _logger.LogInformation(
                "Ignoring settlement message {MessageId}: the integration is not configured.", message.MessageId);
            return;
        }

        if (message.Chat.Id != _options.AllowedChatId)
        {
            await RecordAsync(update, SettlementOutcomes.RejectedChat, "The message came from a chat this channel does not serve.", null, null, null, ct);
            return;
        }

        if (message.From.IsBot || !_options.MayAccept(message.From.Id))
        {
            // The webhook secret proved Telegram sent this. It proved nothing about who pressed send,
            // so this check is the actual boundary. A refusal is recorded with its sender: a sender
            // refused repeatedly is what an attempt to use this channel for something else looks like.
            await RecordAsync(
                update, SettlementOutcomes.RejectedSender,
                $"Telegram user {message.From.Id} is not on the settlement channel's allow list.",
                null, null, null, ct);

            _logger.LogWarning(
                "Refused a settlement message {MessageId} from unlisted Telegram user {TelegramUserId}.",
                message.MessageId, message.From.Id);
            return;
        }

        // Idempotency, checked before anything is written. Telegram redelivers an update on any
        // non-2xx, and the unique index is the authority if two deliveries race.
        var already = await _context.SettlementMessages
            .AnyAsync(m => m.ChatId == message.Chat.Id && m.MessageId == message.MessageId, ct);
        if (already)
        {
            _logger.LogInformation(
                "Settlement message {MessageId} was already recorded; ignoring the redelivery.", message.MessageId);
            return;
        }

        var parse = SettlementMessageParser.Parse(message.Text);
        if (!parse.Success)
        {
            await RecordAsync(update, SettlementOutcomes.Unparseable, parse.Rejection, null, null, null, ct);
            return;
        }

        var amount = parse.Amount!.Value;
        var code = parse.Code!;

        // The customer the code resolves to. A soft-deleted account is not a customer we can pay, and
        // users carries a global !IsDeleted filter, so a deleted one simply does not resolve.
        var userId = await _context.Users
            .AsNoTracking()
            .Where(u => u.SettlementCode == code)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        var payment = BuildPayment(update, amount, userId);

        if (userId is null)
        {
            // The money is real and belongs to nobody we can find. A payment with no order and no
            // customer is the correct record - not a rejection, which would lose the fact that the
            // money arrived at all.
            var paymentId = await WriteAsync(payment, orderId: null, ct);
            await RecordAsync(
                update, SettlementOutcomes.Unallocated,
                $"No customer has the code {code}. The money is recorded and left for an operator.",
                amount, code, userId, ct, paymentId);
            return;
        }

        var openOrders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId.Value && o.Status == OrderStatus.PendingPayment)
            .Select(o => new { o.Id, o.Price })
            .ToListAsync(ct);

        // Exactly one order, for exactly this amount: the one case where attaching is not a guess.
        if (openOrders.Count == 1 && openOrders[0].Price == amount)
        {
            var attachedId = await WriteAsync(payment, openOrders[0].Id, ct);
            await RecordAsync(update, SettlementOutcomes.Accepted, null, amount, code, userId, ct, attachedId);
            return;
        }

        var heldId = await WriteAsync(payment, orderId: null, ct);

        var reason = openOrders.Count switch
        {
            0 => $"The money is held for this customer. They have no order waiting for payment.",
            1 => "The money is held for this customer. Their one open order is for a different amount.",
            _ => $"The money is held for this customer. They have {openOrders.Count} orders waiting for payment, "
                 + "so which one it pays for is not something to guess.",
        };

        await RecordAsync(update, SettlementOutcomes.Unallocated, reason, amount, code, userId, ct);
    }

    private Payment BuildPayment(TelegramUpdate update, decimal amount, Guid? userId) => new()
    {
        Id = Guid.CreateVersion7(),
        Kind = PaymentKind.Capture,
        // Declared, not acquired: no invoice, no webhook, no acquirer. The evidence is who said so,
        // which is why this row carries an author.
        Provider = PaymentProviders.Declared,
        UserId = userId,
        AmountUah = amount,
        AmountKopecks = Money.ToKopecks(amount),
        Currency = "UAH",
        // Already in the account. There is no invoice to wait for, so Succeeded with a paid-at is a
        // statement of fact rather than an optimistic guess.
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = DateTimeOffset.FromUnixTimeSeconds(update.Message!.Date).UtcDateTime,
        CreatedByUserName = update.Message.From!.DisplayName,
        IsAutomatic = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    /// <summary>
    /// Writes the payment and nothing else. The audit row is the caller's to write, because the caller
    /// is what knows the outcome - and a row written here would either duplicate it or record an
    /// outcome the handler has not decided yet.
    /// </summary>
    private async Task<Guid> WriteAsync(Payment payment, Guid? orderId, CancellationToken ct)
    {
        payment.OrderId = orderId;
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(ct);
        return payment.Id;
    }

    private async Task RecordAsync(
        TelegramUpdate update, string outcome, string? reason,
        decimal? amount, string? code, Guid? userId, CancellationToken ct, Guid? paymentId = null)
    {
        _context.SettlementMessages.Add(BuildRow(update, outcome, reason, amount, code, userId, paymentId));
        await _context.SaveChangesAsync(ct);

        _logger.Log(
            outcome == SettlementOutcomes.Accepted ? LogLevel.Information : LogLevel.Warning,
            "Settlement message {MessageId} -> {Outcome} (payment {PaymentId}): {Reason}",
            update.Message!.MessageId, outcome, paymentId, reason);
    }

    private static SettlementMessage BuildRow(
        TelegramUpdate update, string outcome, string? reason,
        decimal? amount, string? code, Guid? userId, Guid? paymentId) => new()
        {
            Id = Guid.CreateVersion7(),
            ChatId = update.Message!.Chat!.Id,
            MessageId = update.Message.MessageId,
            TelegramUserId = update.Message.From!.Id,
            TelegramDisplayName = update.Message.From.DisplayName,
            Text = update.Message.Text?.Trim(),
            // Telegram's clock, not ours: when the money was declared, not when we got round to it.
            SentAtUtc = DateTimeOffset.FromUnixTimeSeconds(update.Message.Date).UtcDateTime,
            Outcome = outcome,
            RejectionReason = reason is null or null ? null : reason.Length > 500 ? reason[..500] : reason,
            AmountUah = amount,
            Code = code,
            ResolvedUserId = userId,
            PaymentId = paymentId,
            CreatedAtUtc = DateTime.UtcNow,
        };
}