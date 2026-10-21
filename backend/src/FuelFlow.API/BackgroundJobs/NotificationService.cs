using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

public sealed class NotificationService
{
    // The outbox producer serialises payloads with camelCase keys (anonymous objects
    // in FulfillmentService), so the consumer must read them case-insensitively. Without
    // this, the PascalCase UserId binds to nothing and stays null, and every event is
    // silently marked processed with no notification written. Web defaults are
    // case-insensitive.
    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly IExpoPushSender _pushSender;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        IExpoPushSender pushSender,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _pushSender = pushSender;
        _logger = logger;
    }

    public async Task ProcessOrderFulfilledEventsAsync(CancellationToken cancellationToken = default)
    {
        // The safety net. The fast path is the job the outbox interceptor queued when this row
        // was written, and it claims the event within seconds. This waits longer than that, so the
        // two never race for the same row - which for a notification means the customer gets it
        // twice.
        var claimCutoff = SystemClock.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow;

        var events = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.OrderFulfilled && e.CreatedAtUtc < claimCutoff)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!events.Any())
        {
            _logger.LogDebug("No pending ORDER_FULFILLED events to process");
            return;
        }

        _logger.LogInformation("Processing {Count} ORDER_FULFILLED events", events.Count);

        foreach (var outboxEvent in events)
        {
            try
            {
                await ProcessEventAsync(outboxEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process ORDER_FULFILLED event {EventId}", outboxEvent.Id);
            }
        }
    }

    /// <summary>
    /// #150 W3 - tells a worker that fuel was issued to them.
    /// <para>
    /// Separate from the order-fulfilled path on purpose. A worker has no order of their own: the
    /// order belongs to the employer, and the worker never sees it. Reusing the customer's copy would
    /// name an order they cannot open, and reusing the customer's push <c>type</c> would deep-link
    /// them into a screen that is not theirs.
    /// </para>
    /// </summary>
    public async Task ProcessVoucherIssuedEventsAsync(CancellationToken cancellationToken = default)
    {
        // The safety net - see ProcessOrderFulfilledEventsAsync.
        var claimCutoff = SystemClock.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow;

        var events = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.VoucherIssued && e.CreatedAtUtc < claimCutoff)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!events.Any())
        {
            _logger.LogDebug("No pending VOUCHER_ISSUED events to process");
            return;
        }

        _logger.LogInformation("Processing {Count} VOUCHER_ISSUED events", events.Count);

        foreach (var outboxEvent in events)
        {
            try
            {
                await ProcessVoucherIssuedEventAsync(outboxEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process VOUCHER_ISSUED event {EventId}", outboxEvent.Id);
            }
        }
    }

    public async Task ProcessVoucherIssuedEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<VoucherIssuedPayload>(
            outboxEvent.Payload, PayloadJsonOptions);

        if (payload?.WorkerUserId == null || !Guid.TryParse(payload.WorkerUserId, out var workerId))
        {
            _logger.LogWarning("Invalid VOUCHER_ISSUED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = workerId,
            // Worker-context copy: speaks about the company that issued the fuel, never about an
            // order, because there is no order on the worker's side to refer to.
            Title = "Вам видано паливо",
            Message = payload.CompanyName is { Length: > 0 } company
                ? $"{company} видав вам {DescribeVoucherCount(payload.VoucherCount)}. Це паливо для роботи — його не потрібно купувати."
                : $"Вам видано {DescribeVoucherCount(payload.VoucherCount)}.",
            // The company is what the row points at. A worker may work for several companies (W1),
            // so the in-app row has to say which one the fuel came from - the same reason the push
            // carries it.
            Type = NotificationTypes.VoucherIssued,
            Payload = NotificationPayload.Serialize(new NotificationPayload
            {
                LegalEntityId = payload.LegalEntityId,
                VoucherCount = payload.VoucherCount,
            }),
            IsRead = false,
            CreatedAtUtc = SystemClock.UtcNow
        };

        _context.Notifications.Add(notification);

        outboxEvent.Processed = true;
        outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
        _context.OutboxEvents.Update(outboxEvent);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created voucher-issued notification for worker {WorkerId} ({Count} voucher(s))",
            workerId, payload.VoucherCount);

        // After the save, for the same reason as the order-fulfilled path: a push failure can then
        // neither duplicate the notification nor cause the event to be reprocessed.
        await TrySendPushAsync(
            workerId,
            notification.Title,
            notification.Message,
            new Dictionary<string, object>
            {
                ["type"] = "voucher_issued",
                // The company is what makes this deep-link correct: a worker in several companies
                // (W1) has to land in the context of the one that issued the fuel, not in whichever
                // context their app happened to have open.
                ["legalEntityId"] = payload.LegalEntityId ?? string.Empty,
                ["voucherCount"] = payload.VoucherCount,
            },
            cancellationToken);
    }

    /// <summary>
    /// Ukrainian plural: 1 талон, 2-4 талони, 5+ талонів. The teens are the trap - 11 is
    /// "талонів" even though it ends in 1, and 12-14 are "талонів" even though they end in 2-4.
    /// </summary>
    private static string DescribeVoucherCount(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;

        if (mod10 == 1 && mod100 != 11)
            return $"{count} талон";

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return $"{count} талони";

        return $"{count} талонів";
    }

    public async Task ProcessEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderFulfilledPayload>(
            outboxEvent.Payload, PayloadJsonOptions);

        if (payload?.UserId == null)
        {
            _logger.LogWarning("Invalid ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!Guid.TryParse(payload.UserId, out var userId))
        {
            _logger.LogWarning("Invalid UserId in ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = "Замовлення виконано",
            Message = $"Ваше замовлення #{payload.OrderId} виконано. Ваучери призначені та готові до використання.",
            // The order id is in the payload as well as in the copy: the copy is prose, the payload
            // is the contract. The client can open the order without scraping its id out of a
            // sentence.
            Type = NotificationTypes.OrderFulfilled,
            Payload = NotificationPayload.Serialize(new NotificationPayload
            {
                OrderId = payload.OrderId.ToString(),
            }),
            IsRead = false,
            CreatedAtUtc = SystemClock.UtcNow
        };

        _context.Notifications.Add(notification);

        outboxEvent.Processed = true;
        outboxEvent.ProcessedAtUtc = SystemClock.UtcNow;
        _context.OutboxEvents.Update(outboxEvent);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created notification for user {UserId}, order {OrderId}", payload.UserId, payload.OrderId);

        // The in-app notification is now persisted and the event is marked processed, so a push
        // failure can neither duplicate the notification nor cause reprocessing. Fire the push as
        // a best-effort follow-up.
        await TrySendPushAsync(
            userId,
            notification.Title,
            notification.Message,
            new Dictionary<string, object>
            {
                ["type"] = "order_fulfilled",
                ["orderId"] = payload.OrderId.ToString(),
            },
            cancellationToken);
    }

    /// <summary>
    /// Pushes a freshly created notification to every active device of the user. Best-effort: any
    /// failure is logged and swallowed so it can never break notification processing. Tokens Expo
    /// reports as DeviceNotRegistered (app uninstalled, token rotated) are deactivated so we stop
    /// targeting them.
    /// <para>
    /// The <c>data</c> payload carries <c>type: "order_fulfilled"</c> and the <c>orderId</c> (as a
    /// string) so a tapped push deep-links to that specific order in the mobile wallet instead of
    /// the generic notifications list. The mobile tap handler branches on these — see
    /// <c>mobile/src/core/notifications/notificationResponse.ts</c>.
    /// </para>
    /// </summary>
    private async Task TrySendPushAsync(
        Guid userId,
        string title,
        string body,
        IReadOnlyDictionary<string, object> data,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokens = await _context.PushTokens
                .Where(t => t.UserId == userId && t.IsActive)
                .ToListAsync(cancellationToken);

            if (tokens.Count == 0)
                return;

            var messages = tokens
                .Select(t => new ExpoPushMessage(
                    t.Token,
                    title,
                    body,
                    data))
                .ToList();

            var results = await _pushSender.SendAsync(messages, cancellationToken);

            var dead = results
                .Where(r => r.Status == ExpoPushStatus.Error
                            && string.Equals(r.ErrorCode, "DeviceNotRegistered", StringComparison.Ordinal))
                .Select(r => r.Token)
                .ToHashSet(StringComparer.Ordinal);

            if (dead.Count == 0)
                return;

            var now = SystemClock.UtcNow;
            foreach (var token in tokens.Where(t => dead.Contains(t.Token)))
            {
                token.IsActive = false;
                token.UpdatedAtUtc = now;
                // Global NoTracking: re-attach as Modified or SaveChanges silently no-ops.
                _context.PushTokens.Update(token);
            }

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Deactivated {Count} unregistered push token(s) for user {UserId}", dead.Count, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send push notification for user {UserId}", userId);
        }
    }
}
