using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.RegisterDevice;

public sealed class RegisterDeviceCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RegisterDeviceCommandHandler> _logger;

    public RegisterDeviceCommandHandler(
        ApplicationDbContext context,
        ILogger<RegisterDeviceCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<RegisterDeviceResponse> HandleAsync(
        RegisterDeviceCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Registering device {DeviceId} for user {UserId}", command.DeviceId, command.UserId);

        var existingDevice = await _context.Devices
            .FirstOrDefaultAsync(
                d => d.DeviceId == command.DeviceId,
                cancellationToken);

        if (existingDevice != null)
        {
            existingDevice.PublicKey = command.PublicKey;
            existingDevice.DeviceModel = command.DeviceModel;
            existingDevice.OsVersion = command.OsVersion;
            existingDevice.AppVersion = command.AppVersion;
            existingDevice.Status = DeviceStatus.Active;
            existingDevice.LastSeenAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Device {DeviceId} re-registered for user {UserId}", command.DeviceId, command.UserId);

            return new RegisterDeviceResponse
            {
                DeviceIdGuid = existingDevice.Id,
                DeviceId = existingDevice.DeviceId,
                Status = existingDevice.Status.ToString(),
                RegisteredAt = existingDevice.CreatedAt
            };
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            DeviceId = command.DeviceId,
            PublicKey = command.PublicKey,
            DeviceModel = command.DeviceModel,
            OsVersion = command.OsVersion,
            AppVersion = command.AppVersion,
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device {DeviceId} registered successfully for user {UserId}", command.DeviceId, command.UserId);

        return new RegisterDeviceResponse
        {
            DeviceIdGuid = device.Id,
            DeviceId = device.DeviceId,
            Status = device.Status.ToString(),
            RegisteredAt = device.CreatedAt
        };
    }
}
