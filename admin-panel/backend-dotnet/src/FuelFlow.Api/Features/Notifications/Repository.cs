using Dapper;
using FuelFlow.Api.Infrastructure;

namespace FuelFlow.Api.Features.Notifications;

public interface INotificationRepository
{
    Task<IEnumerable<Notification>> GetUserNotificationsAsync(string userId);
    Task<Notification> CreateAsync(CreateNotificationRequest request);
    Task MarkAsReadAsync(int id);
}

public class NotificationRepository : INotificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public NotificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Notification>> GetUserNotificationsAsync(string userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<Notification>(
            @"SELECT id, user_id as UserId, title, message, read, created_at as CreatedAt 
              FROM notifications 
              WHERE user_id = @UserId 
              ORDER BY created_at DESC",
            new { UserId = userId });
    }

    public async Task<Notification> CreateAsync(CreateNotificationRequest request)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<Notification>(
            @"INSERT INTO notifications (user_id, title, message) 
              VALUES (@UserId, @Title, @Message) 
              RETURNING id, user_id as UserId, title, message, read, created_at as CreatedAt",
            request);
    }

    public async Task MarkAsReadAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE notifications SET read = 1 WHERE id = @Id",
            new { Id = id });
    }
}
