using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Auth.DeleteUser;

public sealed record DeleteUserCommand(
    Guid UserId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);

public sealed class DeleteUserCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeleteUserCommandHandler> _logger;
    private readonly ProviderEventService _eventService;

    public DeleteUserCommandHandler(
        ApplicationDbContext context,
        ILogger<DeleteUserCommandHandler> logger,
        ProviderEventService eventService)
    {
        _context = context;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task<DeleteUserResult> HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null || user.IsDeleted)
        {
            return new DeleteUserResult { Success = false, Error = "User not found" };
        }

        user.IsDeleted = true;
        user.IsActive = false;
        user.TokenVersion++;
        user.UpdatedAtUtc = DateTime.UtcNow;
        _context.Users.Update(user);

        var devices = await _context.Devices
            .Where(d => d.UserId == command.UserId && d.Status == DeviceStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            device.Status = DeviceStatus.Revoked;
            _context.Devices.Update(device);
        }

        var refreshTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == command.UserId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAtUtc = DateTime.UtcNow;
            _context.RefreshTokens.Update(refreshToken);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} soft-deleted (is_active=false, token_version={Version}, devices/refresh tokens revoked)",
            user.Id, user.TokenVersion);

        var adminId = command.ActingAdminUserId ?? user.Id;
        await _eventService.RecordEventAsync(
            "User",
            user.Id.ToString(),
            "UserDeleted",
            JsonSerializer.Serialize(new { user.Id, user.PhoneNumber }),
            "{}",
            adminId,
            command.ActingAdminName,
            $"Deleted user {user.PhoneNumber}",
            adminId.ToString(),
            cancellationToken);

        return new DeleteUserResult { Success = true };
    }
}

public sealed class DeleteUserResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
