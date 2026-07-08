using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Notifications.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler
{
    private readonly ApplicationDbContext _context;

    public MarkNotificationReadCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MarkNotificationReadResponse?> HandleAsync(
        MarkNotificationReadCommand command,
        CancellationToken cancellationToken = default)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == command.NotificationId && n.UserId == command.UserId, cancellationToken);

        if (notification == null)
            return null;

        notification.IsRead = true;
        await _context.SaveChangesAsync(cancellationToken);

        return new MarkNotificationReadResponse(notification.Id, notification.IsRead);
    }
}
