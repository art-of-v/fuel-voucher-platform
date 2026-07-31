using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

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
        var keyFingerprint = ComputeKeyFingerprint(command.PublicKey);
        _logger.LogInformation(
            "Registering device {DeviceId} for user {UserId} keyFingerprint={KF}",
            command.DeviceId, command.UserId, keyFingerprint);

        var existingDevice = await _context.Devices
            .FirstOrDefaultAsync(
                d => d.DeviceId == command.DeviceId,
                cancellationToken);

        if (existingDevice != null)
        {
            var oldUserId = existingDevice.UserId;
            existingDevice.UserId = command.UserId;
            existingDevice.PublicKey = command.PublicKey;
            existingDevice.DeviceModel = command.DeviceModel;
            existingDevice.OsVersion = command.OsVersion;
            existingDevice.AppVersion = command.AppVersion;
            existingDevice.Status = DeviceStatus.Active;
            existingDevice.LastSeenAt = DateTime.UtcNow;

            _context.Devices.Update(existingDevice);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Device {DeviceId} re-registered for user {UserId} (was bound to user {OldUserId})",
                command.DeviceId, command.UserId, oldUserId);

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

    private static string ComputeKeyFingerprint(string? publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return "empty";

        try
        {
            var base64Only = new string(publicKeyPem.Where(c => !char.IsWhiteSpace(c)).ToArray());
            var keyBytes = Convert.FromBase64String(base64Only);
            return Convert.ToHexString(SHA256.HashData(keyBytes))[..16];
        }
        catch
        {
            return "invalid-base64";
        }
    }
}
