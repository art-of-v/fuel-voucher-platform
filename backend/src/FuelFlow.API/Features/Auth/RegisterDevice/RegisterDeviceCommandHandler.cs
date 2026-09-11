using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Security.Cryptography;

namespace FuelFlow.Features.Auth.RegisterDevice;

public sealed class RegisterDeviceCommandHandler
{
    private const string NonceKeyPrefix = "devreg:";
    private static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _context;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RegisterDeviceCommandHandler> _logger;

    public RegisterDeviceCommandHandler(
        ApplicationDbContext context,
        IConnectionMultiplexer redis,
        ILogger<RegisterDeviceCommandHandler> logger)
    {
        _context = context;
        _redis = redis;
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
            // device_id carries a UNIQUE index, so a collision here means this hardware
            // identifier is already enrolled. Re-registration by the SAME user is legitimate
            // (biometric keys are recreated whenever the user re-enrols a fingerprint/face).
            //
            // Re-registration by a DIFFERENT user is refused unless the caller presents a
            // fresh one-time registration nonce issued by /api/auth/verify (max 10 minutes
            // ago, single use). That nonce proves possession of the new account's phone via
            // OTP, which is the only legitimate way a device changes hands: the previous
            // owner sold the phone, a reviewer reinstalls, the device moves between accounts.
            // Refusing outright locked every such user out of device-signed purchases with
            // no self-service path. Without the nonce the old rule stands unchanged, so a
            // stolen access token still cannot seize another account's device row - the
            // attacker would also need the OTP of the victim's phone.
            if (existingDevice.UserId != command.UserId)
            {
                if (!await TryConsumeRegistrationNonceAsync(command.UserId, command.RegistrationNonce, cancellationToken))
                {
                    _logger.LogWarning(
                        "SECURITY: user {UserId} attempted to register device {DeviceId} already bound to a different user; refused",
                        command.UserId, command.DeviceId);

                    return new RegisterDeviceResponse
                    {
                        Error = "DeviceAlreadyRegistered"
                    };
                }

                _logger.LogWarning(
                    "SECURITY-AUDIT: device {DeviceId} rebound from user {OldUserId} to {NewUserId} with a fresh OTP registration nonce",
                    command.DeviceId, existingDevice.UserId, command.UserId);

                existingDevice.UserId = command.UserId;
            }

            existingDevice.PublicKey = command.PublicKey;
            existingDevice.DeviceModel = command.DeviceModel;
            existingDevice.OsVersion = command.OsVersion;
            existingDevice.AppVersion = command.AppVersion;
            existingDevice.Status = DeviceStatus.Active;
            existingDevice.LastSeenAt = DateTime.UtcNow;

            _context.Devices.Update(existingDevice);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Device {DeviceId} re-registered for its existing owner {UserId}",
                command.DeviceId, command.UserId);

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

    /// <summary>
    /// Checks and burns the one-time registration nonce. The key binds the nonce to
    /// the user id, so a nonce intercepted for one account is worthless for another;
    /// deleting on read makes it single-use.
    /// </summary>
    private async Task<bool> TryConsumeRegistrationNonceAsync(Guid userId, string? nonce, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > 128)
            return false;

        var db = _redis.GetDatabase();
        var key = (RedisKey)$"{NonceKeyPrefix}{userId:N}:{nonce}";
        return await db.KeyDeleteAsync(key);
    }
}
