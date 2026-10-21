using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.GetNotifications;

public sealed class GetNotificationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetNotificationsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<NotificationDto>> HandleAsync(
        GetNotificationsQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Notifications
            .Where(n => n.UserId == query.UserId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new { n.Id, n.Title, n.Message, n.IsRead, n.CreatedAtUtc, n.Type, n.Payload })
            .ToListAsync(cancellationToken);

        return rows
            .Select(n => new NotificationDto(
                n.Id,
                n.Title,
                n.Message,
                n.IsRead,
                n.CreatedAtUtc,
                n.Type,
                ReadId(n.Payload, "legalEntityId"),
                ReadId(n.Payload, "orderId")))
            .ToList();
    }

    /// <summary>
    /// Reads one id out of the stored payload. A missing key, an unparsable id or a payload that
    /// is not JSON at all all mean the same thing: the notification points at nothing the client
    /// can open, and it is not worth failing the request over.
    /// </summary>
    private static Guid? ReadId(string? payload, string key)
    {
        var id = NotificationPayload.ReadId(payload, key);
        return Guid.TryParse(id, out var parsed) ? parsed : null;
    }
}
