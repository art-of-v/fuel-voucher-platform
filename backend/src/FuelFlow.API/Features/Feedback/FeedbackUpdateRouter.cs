using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Decides what an incoming Telegram update means and does it.
/// <para>
/// Three paths, in this order, and the order is the security property:
/// </para>
/// <list type="number">
/// <item>the staff group - only a message that <em>quotes one of the bot's own posts</em> and comes
/// from an allowlisted id is relayed to a customer; everything else in the group is ignored;</item>
/// <item>a private chat - <c>/start</c> binds the chat to an account when the payload verifies,
/// otherwise the chat simply stays unbound;</item>
/// <item>everything else - stored, acknowledged, and queued for triage.</item>
/// </list>
/// <para>
/// The row is written before anything is sent to Telegram and before the classifier runs, so the
/// webhook's contract is the same whether the classifier, the staff delivery or Telegram itself is
/// having a bad day. All three failures are recorded on the row, none of them lose the message.
/// </para>
/// </summary>
public class FeedbackUpdateRouter
{
    private readonly ApplicationDbContext _context;
    private readonly ITelegramFeedbackClient _bot;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<FeedbackUpdateRouter> _logger;

    public FeedbackUpdateRouter(
        ApplicationDbContext context,
        ITelegramFeedbackClient bot,
        IBackgroundJobClient backgroundJobs,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<FeedbackUpdateRouter> logger)
    {
        _context = context;
        _bot = bot;
        _backgroundJobs = backgroundJobs;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Virtual so a test can pin the webhook's "always answer 2xx once authenticated" contract by
    /// making the pipeline fail the way a database outage would.
    /// </summary>
    public virtual async Task HandleAsync(TelegramUpdate update, CancellationToken ct = default)
    {
        var message = update.Message;
        var chat = message?.Chat;
        if (message?.Chat is null || chat is null)
            return;

        // The feature being switched off must not leave a webhook that 500s: Telegram retries a
        // non-2xx forever, and a permanently-failing webhook is a support incident of its own.
        // The customer is told where to go instead - silence is the one response that loses the
        // feedback they were in the middle of sending.
        if (!_options.Enabled)
        {
            if (chat.IsPrivate)
                await SafeSendAsync(chat.Id, FeedbackBotTexts.Unavailable, ct);
            return;
        }

        if (chat.Id == _options.StaffChatId && _options.StaffChatId != 0)
        {
            await HandleStaffAsync(message, ct);
            return;
        }

        // Anything that is not a private 1-on-1 chat is out of scope: a bot added to some group
        // must not start collecting the group's entire conversation.
        if (!chat.IsPrivate)
            return;

        await HandleCustomerAsync(message, ct);
    }

    private async Task HandleCustomerAsync(TelegramMessage message, CancellationToken ct)
    {
        var chatId = message.Chat!.Id;
        var text = message.Text;

        if (string.IsNullOrWhiteSpace(text))
            return;

        var (command, argument) = SplitCommand(text);

        if (command == "start")
        {
            await BindChatAsync(chatId, argument, ct);
            return;
        }

        if (command == "help")
        {
            await SafeSendAsync(chatId, FeedbackBotTexts.Help, ct);
            return;
        }

        if (command is not null)
            return;

        if (text.Length > _options.MaxMessageLength)
        {
            await SafeSendAsync(chatId, FeedbackBotTexts.TooLong, ct);
            return;
        }

        if (!await WithinRateLimitAsync(chatId, ct))
        {
            await SafeSendAsync(chatId, FeedbackBotTexts.RateLimited, ct);
            return;
        }

        var stored = await StoreAsync(message, text, ct);
        if (stored is null)
        {
            // A redelivery of an update we already handled. Acknowledged again (cheap, and the
            // customer asked a second time) but neither re-stored nor re-queued.
            await SafeSendAsync(chatId, FeedbackBotTexts.Acknowledge, ct);
            return;
        }

        await SafeSendAsync(chatId, FeedbackBotTexts.Acknowledge, ct);

        _backgroundJobs.Enqueue<FeedbackTriageService>(job => job.TriageAsync(stored.Value, CancellationToken.None));
    }

    private async Task HandleStaffAsync(TelegramMessage message, CancellationToken ct)
    {
        var text = message.Text;
        var sender = message.From;

        if (string.IsNullOrWhiteSpace(text) || sender is null)
            return;

        // Gate 1 of 2: the sender must be allowlisted. Being added to the group is a Telegram
        // action anyone with an invite can do; the allowlist is config, so being in the group is
        // deliberately not enough to start messaging customers.
        if (!_options.StaffUserIds.Contains(sender.Id))
            return;

        // Gate 2 of 2: it must be a REPLY to a message this bot posted in the group. The lookup is
        // the check - a staff_message_id only exists for a post we made, so quoting anything else
        // (another human, a pinned notice, an older chat) simply finds nothing.
        var quotedId = message.ReplyToMessage?.MessageId;
        if (quotedId is null)
            return;

        var target = await _context.FeedbackMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.StaffChatId == _options.StaffChatId && m.StaffMessageId == quotedId,
                ct);

        if (target is null)
            return;

        await RelayReplyAsync(target, sender, text, ct);
    }

    /// <summary>
    /// Posts a staff answer into the customer's private chat and records it.
    /// </summary>
    private async Task RelayReplyAsync(
        FeedbackMessage target,
        TelegramUser sender,
        string text,
        CancellationToken ct)
    {
        var reply = new FeedbackMessageReply
        {
            Id = Guid.CreateVersion7(),
            FeedbackMessageId = target.Id,
            StaffUserId = sender.Id,
            StaffUserName = Truncate(sender.DisplayName, 200),
            Text = Clip(text, _options.MaxMessageLength),
            SentAtUtc = SystemClock.UtcNow
        };

        // Plain text on purpose: a staff member's reply is prose they typed, and "Ціна & доставка"
        // under parse_mode HTML would be rejected by Telegram with an opaque 400.
        var deliveredId = await _bot.SendMessageAsync(target.TelegramChatId, reply.Text, ct: ct);
        if (deliveredId is not null)
        {
            reply.DeliveredMessageId = deliveredId;
        }
        else
        {
            reply.DeliveryError = "Telegram refused the delivery (see TelegramFeedbackClient logs).";
        }

        _context.FeedbackMessageReplies.Add(reply);

        var stored = await _context.FeedbackMessages
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == target.Id, ct);

        if (stored is not null && deliveredId is not null)
        {
            stored.StaffRepliedAtUtc = SystemClock.UtcNow;
            if (stored.Status == FeedbackStatus.New)
                stored.Status = FeedbackStatus.Triaged;
        }

        await _context.SaveChangesAsync(ct);
    }

    private async Task BindChatAsync(long chatId, string? payload, CancellationToken ct)
    {
        // Who this chat belongs to *before* this /start does anything. Without it every press of
        // "Написати нам" re-sent the full onboarding copy, and a hand-typed /start told an
        // already-linked customer to go and link their account - advice that is wrong precisely
        // because they already did.
        var alreadyBoundTo = await _context.FeedbackChatBindings
            .AsNoTracking()
            .Where(b => b.TelegramChatId == chatId)
            .Select(b => (Guid?)b.UserId)
            .FirstOrDefaultAsync(ct);

        var userId = FeedbackDeepLink.VerifyPayload(
            payload,
            _options.SigningKey,
            TimeSpan.FromDays(_options.DeepLinkTtlDays),
            SystemClock.UtcNow);

        if (userId is not null)
        {
            // Re-binding is how a user switches accounts on the same Telegram: the chat follows
            // the app, not the other way round. Binding to a soft-deleted or missing account is
            // treated as no binding.
            var exists = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == userId.Value && !u.IsDeleted && u.IsActive, ct);

            if (exists)
            {
                await BindChatToUserAsync(chatId, userId.Value, ct);

                // Same account: nothing changed, so say so in one line. A *different* account is a
                // real event and gets the full onboarding, because the chat just changed hands.
                await SafeSendAsync(
                    chatId,
                    alreadyBoundTo == userId ? FeedbackBotTexts.AlreadyLinked : FeedbackBotTexts.WelcomeBound,
                    ct);
                return;
            }
        }

        await SafeSendAsync(
            chatId,
            alreadyBoundTo is null ? FeedbackBotTexts.WelcomeUnbound : FeedbackBotTexts.AlreadyLinked,
            ct);
    }

    private async Task BindChatToUserAsync(long chatId, Guid userId, CancellationToken ct)
    {
        // One account, one chat: the newest link wins, because a stale link sitting in an old
        // message is not what the customer expects after pressing the button again. The detach is
        // an UPDATE on the binding table and a re-stamp of the historical rows, so a chat that was
        // bound before is genuinely unbound rather than merely shadowed.
        await _context.FeedbackChatBindings
            .Where(b => b.UserId == userId && b.TelegramChatId != chatId)
            .ExecuteDeleteAsync(ct);

        await _context.FeedbackMessages
            .Where(m => m.UserId == userId && m.TelegramChatId != chatId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.UserId, (Guid?)null),
                ct);

        var existing = await _context.FeedbackChatBindings
            .AsTracking()
            .FirstOrDefaultAsync(b => b.TelegramChatId == chatId, ct);

        if (existing is null)
        {
            _context.FeedbackChatBindings.Add(new FeedbackChatBinding
            {
                TelegramChatId = chatId,
                UserId = userId,
                BoundAtUtc = SystemClock.UtcNow
            });
        }
        else
        {
            existing.UserId = userId;
            existing.BoundAtUtc = SystemClock.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        // Messages already written from this chat before the binding existed - only reachable if
        // the account was rebound - inherit it too.
        await _context.FeedbackMessages
            .Where(m => m.TelegramChatId == chatId && m.UserId == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.UserId, (Guid?)userId),
                ct);
    }

    /// <summary>
    /// Persists the message and returns its id, or null when this update was already stored.
    /// The unique (chat, message id) index is the arbiter; the pre-check only avoids taking an
    /// exception on the common duplicate path.
    /// </summary>
    private async Task<Guid?> StoreAsync(TelegramMessage message, string text, CancellationToken ct)
    {
        var chatId = message.Chat!.Id;

        var alreadyStored = await _context.FeedbackMessages
            .AsNoTracking()
            .AnyAsync(m => m.TelegramChatId == chatId && m.TelegramMessageId == message.MessageId, ct);

        if (alreadyStored)
            return null;

        var userId = await _context.FeedbackChatBindings
            .AsNoTracking()
            .Where(b => b.TelegramChatId == chatId)
            .Select(b => (Guid?)b.UserId)
            .FirstOrDefaultAsync(ct);

        var row = new FeedbackMessage
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TelegramChatId = chatId,
            TelegramMessageId = message.MessageId,
            Text = Clip(text, _options.MaxMessageLength),
            Status = FeedbackStatus.New,
            CreatedAtUtc = SystemClock.UtcNow
        };

        _context.FeedbackMessages.Add(row);

        try
        {
            await _context.SaveChangesAsync(ct);
            return row.Id;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogInformation(
                ex,
                "Feedback update {MessageId} in chat {ChatId} was a redelivery; not stored twice.",
                message.MessageId, chatId);

            // The change tracker still holds the failed Add; a later SaveChanges in this scope
            // would try to insert it again.
            _context.Entry(row).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// Hourly ceiling per customer chat. Counted from the table rather than a cache so a restart
    /// cannot hand everybody a fresh allowance.
    /// </summary>
    private async Task<bool> WithinRateLimitAsync(long chatId, CancellationToken ct)
    {
        var since = SystemClock.UtcNow.AddHours(-1);
        var recent = await _context.FeedbackMessages
            .AsNoTracking()
            .CountAsync(m => m.TelegramChatId == chatId && m.CreatedAtUtc >= since, ct);

        return recent < _options.MinMessagesPerHour;
    }

    private async Task SafeSendAsync(long chatId, string text, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            return;

        await _bot.SendMessageAsync(chatId, text, ct: ct);
    }

    /// <summary>
    /// Splits "/start abc-def" into ("start", "abc-def"). Returns (null, text) for ordinary text,
    /// so a message that merely begins with a slash but is not a known command is treated as
    /// feedback rather than silently dropped.
    /// </summary>
    private static (string? Command, string? Argument) SplitCommand(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('/'))
            return (null, trimmed);

        var space = trimmed.IndexOf(' ');
        var command = (space < 0 ? trimmed : trimmed[..space]).TrimStart('/').ToLowerInvariant();

        // A bot mention is appended by Telegram when the command was typed in a group.
        var at = command.IndexOf('@');
        if (at >= 0)
            command = command[..at];

        if (command is not ("start" or "help"))
            return (null, trimmed);

        return (command, space < 0 ? null : trimmed[(space + 1)..].Trim());
    }

    /// <summary>Not a bot id - Telegram's numeric bot id is not in the update payload. Kept out of
    /// the real check on purpose; see the lookup in <see cref="HandleStaffAsync"/>.</summary>
    private static long BotIdGuess() => 0;

    /// <summary>
    /// Clips to <paramref name="maxLength"/>. The overload taking a nullable value is for the one
    /// caller that genuinely has one — <c>TelegramMessage.From.DisplayName</c> is <c>string?</c> and
    /// lands on a nullable column.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : Clip(value, maxLength);

    /// <summary>
    /// Same clip for a value already known to be non-null, typed so it cannot hand back null. A
    /// single nullable signature served both callers, which made the compiler flag every
    /// non-nullable assignment even though <see cref="string.IsNullOrEmpty"/> on a non-null input
    /// can only ever return that input — the warning was noise that trains people to ignore the
    /// real ones. C# cannot overload on nullability alone (CS0111), hence the distinct name.
    /// </summary>
    private static string Clip(string value, int maxLength) =>
        value[..Math.Min(value.Length, maxLength)];
}