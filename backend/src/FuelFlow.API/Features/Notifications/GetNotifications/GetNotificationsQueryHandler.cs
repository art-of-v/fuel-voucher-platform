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
        return await _context.Notifications
            .Where(n => n.UserId == query.UserId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.IsRead, n.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
