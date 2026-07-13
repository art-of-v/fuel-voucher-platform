using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.JobsWorker.Models;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.JobsWorker.Services;

public sealed class NotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
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
        var payload = System.Text.Json.JsonSerializer.Deserialize<OrderFulfilledPayload>(outboxEvent.Payload);

        if (payload?.UserId == null)
        {
            _logger.LogWarning("Invalid ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!Guid.TryParse(payload.UserId, out var userId))
        {
            _logger.LogWarning("Invalid UserId in ORDER_FULFILLED payload for event {EventId}", outboxEvent.Id);
            outboxEvent.Processed = true;
            outboxEvent.ProcessedAtUtc = DateTime.UtcNow;
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

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created notification for user {UserId}, order {OrderId}", payload.UserId, payload.OrderId);
    }
}
