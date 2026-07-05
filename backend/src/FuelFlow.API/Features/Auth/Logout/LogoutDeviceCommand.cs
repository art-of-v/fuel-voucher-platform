using FuelFlow.Persistence;
using FuelFlow.Features.Auth.SharedModels;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.Logout;

public sealed record LogoutDeviceCommand(string DeviceId, Guid UserId);

public sealed class LogoutDeviceCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<LogoutDeviceCommandHandler> _logger;

    public LogoutDeviceCommandHandler(
        ApplicationDbContext context,
        ILogger<LogoutDeviceCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(LogoutDeviceCommand command, CancellationToken cancellationToken)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == command.DeviceId && d.UserId == command.UserId, cancellationToken);

        if (device is not null)
        {
            device.Status = DeviceStatus.Revoked;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Device {DeviceId} revoked for user {UserId}", command.DeviceId, command.UserId);
        }
    }
}
