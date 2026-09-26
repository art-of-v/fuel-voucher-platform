using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
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
        var events = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.OrderFulfilled)
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

    private async Task ProcessEventAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderFulfilledPayload>(
            outboxEvent.Payload, PayloadJsonOptions);

        if (payload?.UserId == null)
        {
            _logger.LogWarning("Invalid ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!Guid.TryParse(payload.UserId, out var userId))
        {
            _logger.LogWarning("Invalid UserId in ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            _context.OutboxEvents.Update(outboxEvent);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "Замовлення виконано",
            Message = $"Ваше замовлення #{payload.OrderId} виконано. Ваучери призначені та готові до використання.",
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);

        outboxEvent.Processed = true;
        outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
        _context.OutboxEvents.Update(outboxEvent);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created notification for user {UserId}, order {OrderId}", payload.UserId, payload.OrderId);

        // The in-app notification is now persisted and the event is marked processed, so a push
        // failure can neither duplicate the notification nor cause reprocessing. Fire the push as
        // a best-effort follow-up.
        await TrySendPushAsync(userId, notification.Title, notification.Message, cancellationToken);
    }

    /// <summary>
    /// Pushes a freshly created notification to every active device of the user. Best-effort: any
    /// failure is logged and swallowed so it can never break notification processing. Tokens Expo
    /// reports as DeviceNotRegistered (app uninstalled, token rotated) are deactivated so we stop
    /// targeting them.
    /// </summary>
    private async Task TrySendPushAsync(Guid userId, string title, string body, CancellationToken cancellationToken)
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
                    new Dictionary<string, object> { ["type"] = "notification" }))
                .ToList();

            var results = await _pushSender.SendAsync(messages, cancellationToken);

            var dead = results
                .Where(r => r.Status == ExpoPushStatus.Error
                            && string.Equals(r.ErrorCode, "DeviceNotRegistered", StringComparison.Ordinal))
                .Select(r => r.Token)
                .ToHashSet(StringComparer.Ordinal);

            if (dead.Count == 0)
                return;

            var now = DateTime.UtcNow;
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
